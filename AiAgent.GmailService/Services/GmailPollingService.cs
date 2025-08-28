using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

public class GmailPollingService : BackgroundService
{
    private readonly GmailServiceAgent _gmailService;
    private readonly ILogger<GmailPollingService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly Dictionary<long, long> _lastProcessedInternalDate = new();

    public GmailPollingService(
        GmailServiceAgent gmailService,
        ILogger<GmailPollingService> logger,
        IHttpClientFactory httpClientFactory
    )
    {
        _gmailService = gmailService;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("GmailPollingService started");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var googleRefreshTokens = await GetGoogleRefreshTokensAsync();
                foreach (var kvp in googleRefreshTokens)
                {
                    long chatId = kvp.Key;
                    string refreshToken = kvp.Value;

                    // On first run for a chatId, record the latest message's InternalDate and skip processing
                    if (!_lastProcessedInternalDate.ContainsKey(chatId))
                    {
                        var messages = await _gmailService.GetRecentMessagesAsync(refreshToken, 1, stoppingToken);
                        if (messages != null && messages.Any())
                        {
                            _lastProcessedInternalDate[chatId] = messages.First().Message.InternalDate ?? 0;
                        }
                        else
                        {
                            _lastProcessedInternalDate[chatId] = 0; // No messages yet
                        }
                        continue; // skip processing on first cycle
                    }

                    // Fetch recent messages (fetch more to avoid missing any)
                    var messagesWithSenders = await _gmailService.GetRecentMessagesAsync(refreshToken, 10, stoppingToken);
                    if (messagesWithSenders == null) continue;

                    var lastKnownDate = _lastProcessedInternalDate[chatId];

                    // Filter only new messages (by InternalDate)
                    var newMessages = messagesWithSenders
                        .Where(m => m.Message.InternalDate > lastKnownDate)
                        .OrderBy(m => m.Message.InternalDate) // process oldest first
                        .ToList();

                    if (!newMessages.Any()) continue;

                    foreach (var msgWithSender in newMessages)
                    {
                        var msg = msgWithSender.Message;
                        var senderEmail = msgWithSender.SenderEmail;
                        var fullMsg = await _gmailService.GetMessageBodyAsync(refreshToken, msg.Id, stoppingToken);
                        if (string.IsNullOrEmpty(fullMsg)) continue;
                        var aiDecision = await GetAIDecisionAsync(fullMsg, stoppingToken);
                        if (aiDecision.Action == "ignore")
                        {
                            continue;
                        }
                        else if (aiDecision.Action == "auto_reply")
                        {
                            await _gmailService.SendMessageAsync(refreshToken, senderEmail, aiDecision.Subject, aiDecision.Body, stoppingToken);
                        }
                        else if (aiDecision.Action == "escalate")
                        {
                            await EscalateToTelegram(chatId, senderEmail, aiDecision.Subject, fullMsg, aiDecision.SuggestedReply);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GmailPollingService");
            }
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }

    private async Task<Dictionary<long, string>> GetGoogleRefreshTokensAsync()
    {
        var client = _httpClientFactory.CreateClient();
        var response = await client.GetAsync("https://localhost:7411/api/telegrambot/google-tokens"); // Update URL as needed
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        return System.Text.Json.JsonSerializer.Deserialize<Dictionary<long, string>>(json);
    }

    private async Task<AIDecision> GetAIDecisionAsync(string emailText, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(emailText))
        {
            _logger.LogWarning("No email text provided to AI decision.");
            return new AIDecision { Action = "ignore" };
        }

        var client = _httpClientFactory.CreateClient();
        var prompt = $@"You are an email assistant. Given the following email:
- If it is a commercial offer, advertisement, or spam, respond with action 'ignore'.
- If it is a person texting or a casual/personal message, respond with action 'auto_reply' and generate a polite reply.
- If the sender is asking for something important, urgent, or requiring a decision, respond with action 'escalate'.
If you respond with action 'escalate', you must also include a 'suggestedReply' parameter with a suggested reply to the sender.
Respond ONLY in this JSON format: {{""action"":""ignore|auto_reply|escalate"",""to"":""<recipient email>"",""subject"":""<generate subject line>"",""body"":""<generate reply body>"", ""suggestedReply(only if action is ""escalate"")"": ""...""}}.
Email: {emailText}";
        var request = new { prompt };
        var content = new StringContent(JsonSerializer.Serialize(request), System.Text.Encoding.UTF8, "application/json");
        var response = await client.PostAsync("http://localhost:5062/api/OpenAi/ask", content, token);
        if (!response.IsSuccessStatusCode)
            return new AIDecision { Action = "ignore" };
        var responseString = await response.Content.ReadAsStringAsync(token);

        _logger.LogInformation("AI raw response: {Response}", responseString);

        try
        {
            using var aiJsonDoc = JsonDocument.Parse(responseString);
            var openAiContent = aiJsonDoc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
            using var aiDoc = JsonDocument.Parse(openAiContent);
            var root = aiDoc.RootElement;
            return new AIDecision
            {
                Action = root.TryGetProperty("action", out var action) ? action.GetString() : "auto_reply",
                To = root.TryGetProperty("to", out var to) ? to.GetString() : null,
                Subject = root.TryGetProperty("subject", out var subject) ? subject.GetString() : null,
                Body = root.TryGetProperty("body", out var body) ? body.GetString() : null,
                SuggestedReply = root.TryGetProperty("suggestedReply", out var suggestedReply) ? suggestedReply.GetString() : ""
            };
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse AI content as JSON: {Content}", responseString);
            return new AIDecision { Action = "ignore" };
        }
    }

    private async Task EscalateToTelegram(long chatId, string senderEmail, string subject, string body, string suggestedReply)
    {
        var client = _httpClientFactory.CreateClient();
        var requestBody = new
        {
            ChatId = chatId,
            Senderemail = senderEmail,
            Subject = subject,
            Body = body,
            SuggestedReply = suggestedReply 
        };

        var json = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions
        {
            PropertyNamingPolicy = null
        });

        var request = new HttpRequestMessage(HttpMethod.Post, "https://localhost:7411/api/telegrambot/escalate")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        var response = await client.SendAsync(request);

        var responseText = await response.Content.ReadAsStringAsync();
        _logger.LogError($"Status: {response.StatusCode}");
        _logger.LogError($"Response: {responseText}"); // Update URL as needed
    }

    private class AIDecision
    {
        public string Action { get; set; } // ignore, auto_reply, escalate
        public string To { get; set; }
        public string Subject { get; set; }
        public string Body { get; set; }
        public string SuggestedReply { get; set; }
    }

    [ApiController]
    [Route("api/[controller]")]
    public class GmailReplyController : ControllerBase
    {
        private readonly GmailServiceAgent _gmailService;
        public GmailReplyController(GmailServiceAgent gmailService)
        {
            _gmailService = gmailService;
        }

        [HttpPost("reply")]
        public async Task<IActionResult> Reply([FromBody] GmailReplyRequest req)
        {
            // Здесь нужно получить refreshToken пользователя по chatId (например, из БД или конфига)
            // TODO: реализовать получение refreshToken
            await _gmailService.SendMessageAsync(req.RefreshToken, req.To, req.Subject, req.Body, default);
            return Ok();
        }

        public class GmailReplyRequest
        {
            public string RefreshToken { get; set; }
            public string To { get; set; }
            public string Subject { get; set; }
            public string Body { get; set; }
        }
    }
}