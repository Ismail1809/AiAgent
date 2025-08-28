using AiAgent.MobileServiceApi.Data;
using AiAgent.MobileServiceApi.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;

namespace AiAgent.MobileServiceApi.Services
{

    public class ChatApiService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ApplicationDbContext _context;

        public ChatApiService(IHttpClientFactory httpClientFactory, IConfiguration configuration, ApplicationDbContext context)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _context = context;
        }

        // Add a message to the chat and keep only the last 10 messages
        private async Task AddMessageToDbAsync(string chatCode, string message, string sender)
        {
            var chat = await _context.Chats.FirstOrDefaultAsync(c => c.Code == chatCode);
            if (chat == null) return;

            var messages = await _context.ChatMessages
                .Where(m => m.ChatId == chat.Id)
                .OrderBy(m => m.CreatedAt)
                .ToListAsync();
            if (messages.Count >= 10)
            {
                var toRemove = messages.Take(messages.Count - 9).ToList();
                _context.ChatMessages.RemoveRange(toRemove);
                await _context.SaveChangesAsync();
            }

            var chatMessage = new ChatMessage
            {
                ChatId = chat.Id,
                Sender = sender,
                Content = message,
                CreatedAt = DateTime.UtcNow
            };
            _context.ChatMessages.Add(chatMessage);
            await _context.SaveChangesAsync();
        }

        // Get chat history (last 10 messages) as a list of strings
        private async Task<List<string>> GetChatHistoryAsync(string chatCode)
        {
            var chat = await _context.Chats.FirstOrDefaultAsync(c => c.Code == chatCode);
            if (chat == null) return new List<string>();
            return await _context.ChatMessages
                .Where(m => m.ChatId == chat.Id)
                .OrderBy(m => m.CreatedAt)
                .Select(m => $"{m.Sender}: {m.Content}")
                .ToListAsync();
        }

        public async Task<List<ChatMessage>> GetLastMessagesAsync(string chatCode)
        {
            var chat = await _context.Chats.FirstOrDefaultAsync(c => c.Code == chatCode);
            if (chat == null) return new List<ChatMessage>();
            return await _context.ChatMessages
                .Where(m => m.ChatId == chat.Id)
                .OrderBy(m => m.CreatedAt)
                .ToListAsync();
        }

        public async Task<Chat> CreateChatForUserAsync(int userId)
        {
            // Check if user already has a chat
            var userChat = await _context.UserChats.Include(uc => uc.Chat)
                .FirstOrDefaultAsync(uc => uc.UserId == userId);
            if (userChat != null)
                return userChat.Chat;

            // Create new chat
            var chat = new Chat
            {
                Code = Guid.NewGuid().ToString("N").Substring(0, 8),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _context.Chats.Add(chat);
            await _context.SaveChangesAsync();

            var newUserChat = new UserChat
            {
                UserId = userId,
                ChatId = chat.Id,
                CreatedAt = DateTime.UtcNow
            };
            _context.UserChats.Add(newUserChat);
            await _context.SaveChangesAsync();

            return chat;
        }

        public async Task<string> GetChatResponseAsync(string message, string chatCode)
        {
            // Add user message to chat history in DB
            await AddMessageToDbAsync(chatCode, message, "User");
            // Compose prompt from chat history, always include introduction
            var chatHistory = await GetChatHistoryAsync(chatCode);
            var historyPrompt = chatHistory.Count > 0 ? string.Join("\n", chatHistory) : message;
            var currentUtc = DateTime.UtcNow.ToString("u");
            var systemPrompt = @"You are an assistant that helps users schedule meetings and process an e-mail (Gmail) messages (initially you must to introduce yourself to user and you your abilities).
-Messages may come from chat or from e-mail. If the message is an e-mail, treat it as a user request and act accordingly. Always determine the source by context (for example, if the message looks like an e-mail, process it as such).

-You will only schedule if user asks for it, otherwise you can freely talk to him, do not push user to schedule a meeting.

-Do not use other languages (ex. Spanish), only the one user uses.

-Dont' use spanish, never.

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
If the user is not asking to schedule a meeting, or any other messages, reply with a JSON object in the following format:
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

            var aiProxyUrl = _configuration["AiProxyUrl"] ?? "http://localhost:5062/api/OpenAi/ask";
            var calendarUrl = _configuration["GoogleCalendarUrl"] ?? "http://localhost:5277/schedule";
            var outlookCalendarUrl = _configuration["OutlookCalendarUrl"] ?? "https://localhost:7032/api/OutlookCalendar/schedule";

            var client = _httpClientFactory.CreateClient();
            var content = new StringContent(JsonSerializer.Serialize(new { prompt }), Encoding.UTF8, "application/json");
            string aiResponse = "Sorry, I couldn't get a response.";
            try
            {
                var response = await client.PostAsync(aiProxyUrl, content);
                if (response.IsSuccessStatusCode)
                {
                    var responseString = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(responseString);
                    var openAiContent = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();

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
                                        }
                                        string endpoint = isOutlook ? outlookCalendarUrl : calendarUrl;

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


                                                // Find the user for this chat
                                                var chat = await _context.Chats.FirstOrDefaultAsync(c => c.Code == chatCode);
                                                if (chat != null)
                                                {
                                                    var userChat = await _context.UserChats
                                                        .Include(uc => uc.User)
                                                        .FirstOrDefaultAsync(uc => uc.ChatId == chat.Id);

                                                    var refreshToken = userChat?.User?.OutlookRefreshToken;

                                                    if (string.IsNullOrWhiteSpace(refreshToken))
                                                    {
                                                        aiResponse = $"To schedule a meeting in Outlook, please go to the 'Manage Account' menu (hamburger menu → Manage Account) and link your Outlook account first. After linking your account, come back here and I'll help you schedule the meeting.";
                                                    }
                                                    else
                                                    {
                                                        // Use the stored refresh token for scheduling
                                                        var url = endpoint + "?refreshToken=" + Uri.EscapeDataString(refreshToken);
                                                        var scheduleContent = new StringContent(JsonSerializer.Serialize(scheduleRequest), Encoding.UTF8, "application/json");
                                                        var scheduleResponse = await client.PostAsync(url, scheduleContent);
                                                        if (scheduleResponse.IsSuccessStatusCode)
                                                        {
                                                            var scheduleResult = await scheduleResponse.Content.ReadAsStringAsync();
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
                                            }
                                            else
                                            {
                                                var chat = await _context.Chats.FirstOrDefaultAsync(c => c.Code == chatCode);
                                                if (chat != null)
                                                {
                                                    var userChat = await _context.UserChats
                                                        .Include(uc => uc.User)
                                                        .FirstOrDefaultAsync(uc => uc.ChatId == chat.Id);

                                                    var refreshToken = userChat?.User?.GoogleRefreshToken;

                                                    if (string.IsNullOrWhiteSpace(refreshToken))
                                                    {
                                                        aiResponse = $"To schedule a meeting in Google, please go to the 'Manage Account' menu (hamburger menu → Manage Account) and link your Google account first. After linking your account, come back here and I'll help you schedule the meeting.";
                                                    }
                                                    else
                                                    {
                                                        var url = endpoint + "?refreshToken=" + Uri.EscapeDataString(refreshToken);
                                                        var scheduleContent = new StringContent(JsonSerializer.Serialize(scheduleRequest), Encoding.UTF8, "application/json");
                                                        var scheduleResponse = await client.PostAsync(url, scheduleContent);
                                                        if (scheduleResponse.IsSuccessStatusCode)
                                                        {
                                                            var scheduleResult = await scheduleResponse.Content.ReadAsStringAsync();
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
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            aiResponse = $"Sorry, I couldn't schedule the meeting in {(isOutlook ? "outlook" : "google")} due to an error: {ex.Message}";
                                        }
                                    }
                                }
                                else if (action == "ask_for_details")
                                {
                                    aiResponse = root.GetProperty("message").GetString();
                                }
                                else
                                {
                                    aiResponse = root.GetProperty("message").GetString();
                                }
                            }
                            else
                            {
                                aiResponse = root.GetProperty("message").GetString();
                            }
                        }
                        else
                        {
                            aiResponse = root.GetProperty("message").GetString();
                        }
                    }
                    catch (Exception ex)
                    {
                        aiResponse = $"Error: {ex.Message}";
                    }
                    await AddMessageToDbAsync(chatCode, aiResponse, "AI");
                }
            }
            catch (Exception ex)
            {
                aiResponse = $"Error: {ex.Message}";
            }
            return aiResponse;
        }

        public async Task<List<(string ChatCode, string LastMessage)>> GetUserChatsWithSummaryAsync(int userId)
        {
            var userChats = await _context.UserChats
                .Include(uc => uc.Chat)
                .Where(uc => uc.UserId == userId)
                .Select(uc => uc.Chat)
                .ToListAsync();
            var result = new List<(string, string)>();
            foreach (var chat in userChats)
            {
                var lastMsg = await _context.ChatMessages
                    .Where(m => m.ChatId == chat.Id)
                    .OrderByDescending(m => m.CreatedAt)
                    .Select(m => m.Content)
                    .FirstOrDefaultAsync();
                result.Add((chat.Code, lastMsg ?? "New Chat"));
            }
            return result;
        }

        public async Task<Chat> CreateNewChatForUserAsync(int userId)
        {
            var chat = new Chat
            {
                Code = Guid.NewGuid().ToString("N").Substring(0, 8),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _context.Chats.Add(chat);
            await _context.SaveChangesAsync();

            var newUserChat = new UserChat
            {
                UserId = userId,
                ChatId = chat.Id,
                CreatedAt = DateTime.UtcNow
            };
            _context.UserChats.Add(newUserChat);
            await _context.SaveChangesAsync();

            return chat;
        }

        public async Task<Chat> GetUserChatAsync(int userId)
        {
            var userChat = await _context.UserChats
                .Include(uc => uc.Chat)
                .FirstOrDefaultAsync(uc => uc.UserId == userId);
            return userChat?.Chat;
        }

        public async Task<bool> DeleteChatAsync(int userId, string chatCode)
        {
            try
            {
                var chat = await _context.Chats.FirstOrDefaultAsync(c => c.Code == chatCode);
                if (chat == null)
                    return false;

                // Check if the user owns this chat
                var userChat = await _context.UserChats
                    .FirstOrDefaultAsync(uc => uc.ChatId == chat.Id && uc.UserId == userId);
                if (userChat == null)
                    return false;

                // Delete all messages for this chat
                var messages = await _context.ChatMessages
                    .Where(m => m.ChatId == chat.Id)
                    .ToListAsync();
                _context.ChatMessages.RemoveRange(messages);

                // Delete the user-chat relationship
                _context.UserChats.Remove(userChat);

                // Delete the chat
                _context.Chats.Remove(chat);

                await _context.SaveChangesAsync();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
