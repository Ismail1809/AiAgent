using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Calendar.v3;
using Google.Apis.Services;
using Google.Apis.Util;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using System.IO;

public class TelegramBotPollingService : BackgroundService
{
    private readonly ILogger<TelegramBotPollingService> _logger;
    private readonly TelegramBotClient _botClient;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _aiProxyUrl;
    private readonly string _calendarUrl;
    private readonly string _outlookCalendarUrl;
    private static readonly Dictionary<long, List<string>> _chatHistories = new();
    public static readonly Dictionary<long, string> _outlookRefreshTokens = new();
    public static readonly Dictionary<long, string> _googleRefreshTokens = new();
    private static readonly Dictionary<long, EscalatedEmail> _escalatedEmails = new();
    private static readonly HashSet<long> _knownChatIds = new();
    private readonly IConfiguration _config;


    public TelegramBotPollingService(ILogger<TelegramBotPollingService> logger, IConfiguration config, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _config = config;
        var token = config["TelegramBotToken"];
        _botClient = new TelegramBotClient(token);
        _httpClientFactory = httpClientFactory;
        _aiProxyUrl = config["AiProxyUrl"] ?? "http://localhost:5062/api/OpenAi/ask";
        _calendarUrl = config["GoogleCalendarUrl"] ?? "http://localhost:5277/schedule";
        _outlookCalendarUrl = config["OutlookCalendarUrl"] ?? "http://localhost:5046/api/OutlookCalendar/schedule";
        // Remove GetCalendarServiceAsync and related Google API fields
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        int offset = 0;
        _logger.LogInformation("Starting Telegram long polling...");

        while (!stoppingToken.IsCancellationRequested)
        {
            var updates = await _botClient.GetUpdates(offset, timeout: 30, cancellationToken: stoppingToken);

            foreach (var update in updates)
            {
                offset = update.Id + 1;

                if (update.Type == UpdateType.Message && update.Message?.Voice != null)
                {
                    var chatId = update.Message.Chat.Id;
                    var fileId = update.Message.Voice.FileId;
                    // Download the file from Telegram
                    var file = await _botClient.GetFile(fileId, cancellationToken: stoppingToken);
                    using var fileStream = new MemoryStream();
                    await _botClient.DownloadFile(file.FilePath, fileStream, cancellationToken: stoppingToken);
                    fileStream.Position = 0;
                    // Save to disk (OpenAI Whisper API requires a file)
                    var tempOggPath = Path.GetTempFileName() + ".ogg";
                    await File.WriteAllBytesAsync(tempOggPath, fileStream.ToArray(), stoppingToken);
                    // Transcribe with OpenAI Whisper
                    string recognizedText = await TranscribeWithOpenAIWhisperAsync(tempOggPath);
                    // Clean up temp file
                    File.Delete(tempOggPath);
                    // Use recognizedText as the user's message
                    update.Message.Text = recognizedText;
                    // Optionally, send the recognized text back to the user for confirmation
                    //await _botClient.SendMessage(chatId, $"Recognized text: {recognizedText}", cancellationToken: stoppingToken);
                    //// Continue with your normal text message handling logic (the rest of the loop)
                }

                if (update.Type == UpdateType.Message && update.Message?.Text != null)
                {
                    var chatId = update.Message.Chat.Id;
                    var messageText = update.Message.Text;
                    _logger.LogInformation($"Received message: {messageText}");

                    if (_escalatedEmails.TryGetValue(chatId, out var escalated))
                    {
                        if (messageText.StartsWith("/ai_reply"))
                        {
                            // Call AI to generate answer and send via GmailPollingService
                            await SendGmailReply(chatId, escalated.Subject, escalated.SuggestedReply, escalated.SenderEmail);
                            await _botClient.SendMessage(chatId, $"AI answer sent: {escalated.SuggestedReply}");
                            _escalatedEmails.Remove(chatId);
                            return;
                        }
                        if (messageText.StartsWith("/reply "))
                        {
                            var userReply = messageText.Substring("/reply ".Length);
                            // Send user's reply via GmailPollingService
                            await SendGmailReply(chatId, escalated.Subject, userReply, escalated.SenderEmail);
                            await _botClient.SendMessage(chatId, $"Your reply sent: {userReply}");
                            _escalatedEmails.Remove(chatId);
                            return;
                        }
                    }

                    // --- Temporary memory integration ---
                    if (!_chatHistories.ContainsKey(chatId))
                        _chatHistories[chatId] = new List<string>();
                    _chatHistories[chatId].Add($"User: {messageText}");
                    // Keep only the last 10 messages
                    if (_chatHistories[chatId].Count > 10)
                        _chatHistories[chatId] = _chatHistories[chatId].Skip(_chatHistories[chatId].Count - 10).ToList();
                    var historyPrompt = string.Join("\n", _chatHistories[chatId]);
                    // ------------------------------------

                    // Add current UTC time to the prompt
                    var currentUtc = DateTime.UtcNow.ToString("u");
                    var systemPrompt = @"You are an assistant that helps users schedule meetings and process both Telegram and e-mail (Gmail) messages.
Messages may come from Telegram or from e-mail. If the message is an e-mail, treat it as a user request and act accordingly. Always determine the source by context (for example, if the message looks like an e-mail, process it as such).

When the user wants to schedule a meeting, always reply with a JSON object in the following format:
{
  ""isScheduling"": true,
  ""action"": ""schedule_meeting"",
  ""summary"": ""..."",
  ""description"": ""..."",
  ""start"": ""..."",
  ""end"": ""..."",
  ""timeZone"": ""..."",
  ""attendees"": [""...""],
  ""userMessage"": ""..."",
  ""isOutlook"": false, // Only include and set to true if the user explicitly requests Outlook calendar or he metions outlook. Otherwise, omit or set to false for Google Calendar by default.
}
""userMessage"" is ai response to user and in the end of response add for example ""Here is the link to your event: "" in the same lamguage with user .
For outlook calendar, link should just open the calendar, that's it.
If the user does not provide summary, description, or attendees, set them to an empty string (\""\"") or empty array ([]), respectively.
If you do not have enough information, reply with:
{
  ""isScheduling"": true,
  ""action"": ""ask_for_details"",
  ""message"": ""Please provide the missing details: ...""
}
If the user is not asking to schedule a meeting, reply with:
{
  ""isScheduling"": false,
  ""message"": ""...""
}
Do not include any other text outside the JSON object.";
                    var prompt = systemPrompt + "\n\nCurrent UTC time: " + currentUtc + "\n" + historyPrompt + "\n" + @"
Instructions:
- Always reply in the same language as the user's message.
- If the user wants to schedule a meeting, ensure you have all required details (summary, description, start time, end time, time zone, attendees).
- The date and time must be clear, unambiguous, and not in the past. If the date or time is missing, ambiguous, or in the past, ask the user to clarify or provide a valid date and time in their language.
- Always use today's date as 'now' (use a date library to determine the current date, in the user's local time zone if possible).
- Only proceed to schedule the meeting if all required information is clear and valid.
- Otherwise, answer the user's question normally.";

                    var client = _httpClientFactory.CreateClient();
                    var content = new StringContent(JsonSerializer.Serialize(new { prompt }), Encoding.UTF8, "application/json");
                    string aiResponse = "Sorry, I couldn't get a response.";
                    try
                    {
                        var response = await client.PostAsync(_aiProxyUrl, content, stoppingToken);
                        if (response.IsSuccessStatusCode)
                        {
                            var responseString = await response.Content.ReadAsStringAsync(stoppingToken);
                            _logger.LogInformation($"AI response: {responseString}");
                            using var doc = JsonDocument.Parse(responseString);
                            var openAiContent = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
                            _logger.LogInformation($"AI content: {openAiContent}");

                            // Try to parse as JSON for scheduling intent
                            try
                            {
                                using var scheduleDoc = JsonDocument.Parse(openAiContent);
                                var root = scheduleDoc.RootElement;
                                bool isScheduling = root.TryGetProperty("isScheduling", out var isSchedulingProp) && isSchedulingProp.GetBoolean();
                                if (isScheduling)
                                {
                                    if (root.TryGetProperty("action", out var actionProp))
                                    {
                                        var action = actionProp.GetString();
                                        if (action == "schedule_meeting")
                                        {
                                            _logger.LogInformation("AI indicated scheduling intent (isScheduling=true).");
                                            // Check for required fields and validate date/time
                                            bool hasStart = root.TryGetProperty("start", out var startProp);
                                            bool hasEnd = root.TryGetProperty("end", out var endProp);
                                            string startStr = hasStart ? startProp.GetString() : null;
                                            string endStr = hasEnd ? endProp.GetString() : null;
                                            DateTime startDate, endDate;
                                            bool validStart = DateTime.TryParse(startStr, out startDate);
                                            bool validEnd = DateTime.TryParse(endStr, out endDate);
                                            bool isPast = validStart && startDate < DateTime.UtcNow;
                                            bool isAmbiguous = !validStart || !validEnd;
                                            if (!hasStart || !hasEnd || isAmbiguous || isPast)
                                            {
                                                aiResponse = root.TryGetProperty("message", out var msgProp) ? msgProp.GetString() : "Please provide a valid meeting time and duration (not in the past).";
                                            }
                                            else
                                            {

                                                bool isOutlook = false;
                                                if (root.TryGetProperty("isOutlook", out var isOutlookProp))
                                                {
                                                    if (isOutlookProp.ValueKind == JsonValueKind.True)
                                                        isOutlook = true;
                                                    else if (isOutlookProp.ValueKind == JsonValueKind.False)
                                                        isOutlook = false;
                                                    // If isOutlook is present but not true/false, treat as false (Google)
                                                }
                                                // If isOutlook is omitted, default to Google
                                                string endpoint = isOutlook ? _outlookCalendarUrl : _calendarUrl;


                                                // Call GoogleCalService via HTTP POST
                                                var scheduleRequest = new
                                                {
                                                    Summary = root.TryGetProperty("summary", out var summaryProp) ? summaryProp.GetString() : null,
                                                    Description = root.TryGetProperty("description", out var descProp) ? descProp.GetString() : null,
                                                    Start = validStart ? startDate : (DateTime?)null,
                                                    End = validEnd ? endDate : (DateTime?)null,
                                                    TimeZone = root.TryGetProperty("timeZone", out var tzProp) ? tzProp.GetString() : "UTC",
                                                    Attendees = root.TryGetProperty("attendees", out var attendeesProp) && attendeesProp.ValueKind == JsonValueKind.Array
                                                            ? attendeesProp.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrEmpty(x)).ToArray()
                                                            : null
                                                };
                                                try
                                                {
                                                    if (isOutlook)
                                                    {
                                                        if (!_outlookRefreshTokens.TryGetValue(chatId, out var outlookRefreshToken) || string.IsNullOrWhiteSpace(outlookRefreshToken))
                                                        {
                                                            // Provide the user with the Outlook auth link
                                                            var redirectOutlookUri = _config["OAuth:Outlook:RedirectUri"];

                                                            var outlookAuthLink = $"https://login.microsoftonline.com/common/oauth2/v2.0/authorize?client_id=636bc4b3-9e68-4fcc-9ab5-2750ff8bf135&response_type=code&redirect_uri={redirectOutlookUri}&response_mode=query&scope=offline_access%20Calendars.ReadWrite&state={chatId}";

                                                            var replyMarkup = new InlineKeyboardMarkup(InlineKeyboardButton.WithUrl("Authorize Outlook", outlookAuthLink));

                                                            aiResponse = "To schedule a meeting in Outlook, please authorize your Outlook account first.";
                                                            await _botClient.SendMessage(chatId, aiResponse, replyMarkup: replyMarkup, cancellationToken: stoppingToken);
                                                            continue;
                                                        }
                                                        // Add refreshToken as query parameter
                                                        var url = endpoint + "?refreshToken=" + Uri.EscapeDataString(outlookRefreshToken);
                                                        var scheduleContent = new StringContent(JsonSerializer.Serialize(scheduleRequest), Encoding.UTF8, "application/json");
                                                        var scheduleResponse = await client.PostAsync(url, scheduleContent, stoppingToken);
                                                        if (scheduleResponse.IsSuccessStatusCode)
                                                        {
                                                            var scheduleResult = await scheduleResponse.Content.ReadAsStringAsync(stoppingToken);
                                                            using var scheduleResultDoc = JsonDocument.Parse(scheduleResult);
                                                            var createdEvent = scheduleResultDoc.RootElement;
                                                            var htmlLink = createdEvent.TryGetProperty("htmlLink", out var linkProp) ? linkProp.GetString() : null;
                                                            string userMessage = null;
                                                            if (root.TryGetProperty("userMessage", out var userMsgProp))
                                                            {
                                                                userMessage = userMsgProp.GetString();
                                                            }
                                                            if (!string.IsNullOrEmpty(userMessage) && !string.IsNullOrEmpty(htmlLink))
                                                            {
                                                                aiResponse = userMessage + $"\n{htmlLink}";
                                                            }
                                                            else if (!string.IsNullOrEmpty(htmlLink))
                                                            {
                                                                aiResponse = $"Meeting scheduled successfully! Here is your event: {htmlLink}";
                                                            }
                                                            else
                                                            {
                                                                aiResponse = "Meeting scheduled, but no event link was returned.";
                                                            }
                                                        }
                                                        else
                                                        {
                                                            aiResponse = $"Failed to schedule meeting: {scheduleResponse.ReasonPhrase}";
                                                        }
                                                    }
                                                    else
                                                    {
                                                        if (!_googleRefreshTokens.TryGetValue(chatId, out var googleRefreshToken) || string.IsNullOrWhiteSpace(googleRefreshToken))
                                                        {
                                                            var redirectGoogleUri = _config["OAuth:Google:RedirectUri"]; ;

                                                            var googleClientId = _config["OAuth:Google:ClientId"];
                                                            var scope = _config["OAuth:Google:Scope"];

                                                            
                                                            var googleAuthLink = $"https://accounts.google.com/o/oauth2/v2/auth?response_type=code&client_id={googleClientId}&redirect_uri={HttpUtility.UrlEncode(redirectGoogleUri)}&scope={HttpUtility.UrlEncode(scope)}&access_type=offline&prompt=consent&state={chatId}";
                                                            
                                                            var replyMarkup = new InlineKeyboardMarkup(InlineKeyboardButton.WithUrl("Authorize Google", googleAuthLink));

                                                            aiResponse = "To schedule a meeting in Google or accept emails, please authorize your Google account first.";
                                                            await _botClient.SendMessage(chatId, aiResponse, replyMarkup: replyMarkup, cancellationToken: stoppingToken);
                                                            continue;
                                                        }

                                                        // Google calendar logic (existing)
                                                        var url = endpoint + "?refreshToken=" + Uri.EscapeDataString(googleRefreshToken);
                                                        var scheduleContent = new StringContent(JsonSerializer.Serialize(scheduleRequest), Encoding.UTF8, "application/json");
                                                        var scheduleResponse = await client.PostAsync(url, scheduleContent, stoppingToken);
                                                        if (scheduleResponse.IsSuccessStatusCode)
                                                        {
                                                            var scheduleResult = await scheduleResponse.Content.ReadAsStringAsync(stoppingToken);
                                                            using var scheduleResultDoc = JsonDocument.Parse(scheduleResult);
                                                            var createdEvent = scheduleResultDoc.RootElement;
                                                            var htmlLink = createdEvent.TryGetProperty("htmlLink", out var linkProp) ? linkProp.GetString() : null;
                                                            string userMessage = null;
                                                            if (root.TryGetProperty("userMessage", out var userMsgProp))
                                                            {
                                                                userMessage = userMsgProp.GetString();
                                                            }
                                                            if (!string.IsNullOrEmpty(userMessage) && !string.IsNullOrEmpty(htmlLink))
                                                            {
                                                                aiResponse = userMessage + $"\n{htmlLink}";
                                                            }
                                                            else if (!string.IsNullOrEmpty(htmlLink))
                                                            {
                                                                aiResponse = $"Meeting scheduled successfully! Here is your event: {htmlLink}";
                                                            }
                                                            else
                                                            {
                                                                aiResponse = "Meeting scheduled, but no event link was returned.";
                                                            }
                                                        }
                                                        else
                                                        {
                                                            aiResponse = $"Failed to schedule meeting: {scheduleResponse.ReasonPhrase}";
                                                        }
                                                    }
                                                }
                                                catch (Exception ex)
                                                {
                                                    _logger.LogError(ex, "Failed to call GoogleCalService");
                                                    aiResponse = "Sorry, I couldn't schedule the meeting due to an error.";
                                                }
                                            }
                                        }
                                        else if (action == "ask_for_details")
                                        {
                                            aiResponse = root.GetProperty("message").GetString();
                                        }
                                        else
                                        {
                                            aiResponse = root.GetProperty("message").GetString(); ;
                                        }
                                    }
                                    else
                                    {
                                        aiResponse = root.GetProperty("message").GetString(); ;
                                    }
                                }
                                else
                                {
                                    aiResponse = root.GetProperty("message").GetString(); ;
                                }
                            }
                            catch (JsonException)
                            {
                                aiResponse = openAiContent;
                            }
                        }
                        else
                        {
                            aiResponse = $"OpenAI error: {response.StatusCode}";
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error calling AiProxy or Calendar endpoint");
                        aiResponse = "Error contacting AI or calendar service.";
                    }
                    
                    if (_knownChatIds.Add(chatId))
                    {
                        if (!_outlookRefreshTokens.ContainsKey(chatId) || !_googleRefreshTokens.ContainsKey(chatId))
                        {
                            var ngrokClient = new HttpClient();
                            client.DefaultRequestHeaders.Add("ngrok-skip-browser-warning", "true");

                            var redirectOutlookUri = _config["OAuth:Outlook:RedirectUri"];
                            var redirectGoogleUri = _config["OAuth:Google:RedirectUri"]; ;

                            var googleClientId = _config["OAuth:Google:ClientId"];
                            var scope = _config["OAuth:Google:Scope"];

                            var outlookAuthLink = $"https://login.microsoftonline.com/common/oauth2/v2.0/authorize?client_id=636bc4b3-9e68-4fcc-9ab5-2750ff8bf135&response_type=code&redirect_uri={redirectOutlookUri}&response_mode=query&scope=offline_access%20Calendars.ReadWrite&state={chatId}";
                            var googleAuthLink = $"https://accounts.google.com/o/oauth2/v2/auth?response_type=code&client_id={googleClientId}&redirect_uri={HttpUtility.UrlEncode(redirectGoogleUri)}&scope={HttpUtility.UrlEncode(scope)}&access_type=offline&prompt=consent&state={chatId}";

                            var buttons = new List<InlineKeyboardButton[]>();

                            if(!_googleRefreshTokens.ContainsKey(chatId))
                            {
                                buttons.Add([InlineKeyboardButton.WithUrl("Authorize Google", googleAuthLink)]);
                            }
                            if (!_outlookRefreshTokens.ContainsKey(chatId))
                            {
                                buttons.Add([InlineKeyboardButton.WithUrl("Authorize Outlook", outlookAuthLink)]);
                            }

                            var replyMarkup = new InlineKeyboardMarkup(buttons);
                            await _botClient.SendMessage(chatId, "To use Outlook or Google features, please authorize your account:", replyMarkup: replyMarkup, cancellationToken: stoppingToken);
                        }
                    }
                    else
                    {
                        await _botClient.SendMessage(chatId, aiResponse, cancellationToken: stoppingToken);
                    }

                    _chatHistories[chatId].Add($"AI: {aiResponse}");
                    if (_chatHistories[chatId].Count > 10)
                        _chatHistories[chatId] = _chatHistories[chatId].Skip(_chatHistories[chatId].Count - 10).ToList();
                }
            }
        }
    }

    private async Task<string> TranscribeWithOpenAIWhisperAsync(string filePath)
    {
        var apiKey = _config["OpenAiService:OpenApiToken"]; // Replace with your OpenAI API key
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

        using var form = new MultipartFormDataContent();
        using var fileStream = File.OpenRead(filePath);
        form.Add(new StreamContent(fileStream), "file", Path.GetFileName(filePath));
        form.Add(new StringContent("whisper-1"), "model");

        var response = await httpClient.PostAsync("https://api.openai.com/v1/audio/transcriptions", form);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("text").GetString();
    }

    public static Dictionary<long, string> OutlookRefreshTokens => _outlookRefreshTokens;
    public static Dictionary<long, string> GoogleRefreshTokens => _googleRefreshTokens;

    public async Task SendEscalationToUserAsync(long chatId, string senderEmail, string subject, string body, string suggestedReply)
    {
        var message = $"📧 New email:\nSubject: {subject}\n\n{body}\n\n/ai_reply — let AI answer\n/reply <your message> — send your own reply";
        _escalatedEmails[chatId] = new EscalatedEmail { SenderEmail = senderEmail, Subject = subject, Body = body, SuggestedReply = suggestedReply };
        await _botClient.SendMessage(chatId, message);
    }

    private class EscalatedEmail
    {
        public string Subject { get; set; }
        public string Body { get; set; }
        public string SuggestedReply { get; set; }
        public string SenderEmail { get; set; }
    }

    private async Task SendGmailReply(long chatId, string subject, string body, string to)
    {
        var client = _httpClientFactory.CreateClient();
        var req = new
        {
            RefreshToken = _googleRefreshTokens[chatId],
            To = to,
            Subject = subject,
            Body = body
        };
        var content = new System.Net.Http.StringContent(System.Text.Json.JsonSerializer.Serialize(req), System.Text.Encoding.UTF8, "application/json");
        await client.PostAsync("https://localhost:7253/api/GmailService/reply", content); // Update port/URL as needed
    }
}