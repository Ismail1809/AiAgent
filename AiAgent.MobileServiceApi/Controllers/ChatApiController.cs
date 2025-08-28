using AiAgent.MobileServiceApi.Models;
using AiAgent.MobileServiceApi.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Web;

namespace AiAgent.MobileServiceApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ChatApiController : ControllerBase
    {
        private readonly ChatApiService _chatService;

        public ChatApiController(ChatApiService chatService)
        {
            _chatService = chatService;

        }

        public class CreateChatResponse
        {
            public string ChatCode { get; set; }
        }

        public class ChatRequest
        {
            public string Message { get; set; }
            public string ChatCode { get; set; } // Unique code for the chat
        }

        public class OutlookTokenDto
        {
            public string ChatId { get; set; }
            public string RefreshToken { get; set; }
        }

        [HttpPost("send")]
        public async Task<IActionResult> Send([FromBody] ChatRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.Message))
                return BadRequest("Message is required.");
            if (string.IsNullOrWhiteSpace(request?.ChatCode))
                return BadRequest("ChatCode is required.");
            var response = await _chatService.GetChatResponseAsync(request.Message, request.ChatCode);
            return Ok(response);
        }

        [HttpGet("messages/{chatCode}")]
        public async Task<IActionResult> GetMessages(string chatCode)
        {
            var messages = await _chatService.GetLastMessagesAsync(chatCode);
            return Ok(messages.Select(m => new {
                content = m.Content,
                sender = m.Sender,
                createdAt = m.CreatedAt.ToString("o")
            }));
        }

        [HttpGet("user-chats/{userId}")]
        public async Task<IActionResult> GetUserChats(int userId)
        {
            var chats = await _chatService.GetUserChatsWithSummaryAsync(userId);
            return Ok(chats.Select(c => new { chatCode = c.ChatCode, lastMessage = c.LastMessage }));
        }

        [HttpPost("new-chat")]
        public async Task<IActionResult> CreateNewChat([FromBody] int userId)
        {
            var chat = await _chatService.CreateNewChatForUserAsync(userId);
            return Ok(new { chatCode = chat.Code });
        }

        [HttpGet("oauth-link/{provider}/{userId}")]
        public async Task<IActionResult> GetOAuthLink(string provider, int userId)
        {
            var userChat = await _chatService.GetUserChatAsync(userId);
            if (userChat == null)
            {
                return BadRequest(new { error = "User has no active chat" });
            }

            string url;
            if (provider.ToLower() == "google")
            {
                var redirectGoogleUri = "https://39e9bcf6cbed.ngrok-free.app/oauth/callback";
                var googleClientId = "722351317793-pvvn7tgiah8ik3oajucug6oq89t6gbcg.apps.googleusercontent.com";
                var scope = "https://www.googleapis.com/auth/calendar.events https://mail.google.com/";

                url = $"https://accounts.google.com/o/oauth2/v2/auth?response_type=code&client_id={googleClientId}&redirect_uri={HttpUtility.UrlEncode(redirectGoogleUri)}&scope={HttpUtility.UrlEncode(scope)}&access_type=offline&prompt=consent&state={userChat.Code}";
            }
            else if (provider.ToLower() == "outlook")
            {
                var clientId = "636bc4b3-9e68-4fcc-9ab5-2750ff8bf135";
                var redirectUri = "https://bacbba3bc43f.ngrok-free.app/api/OutlookCalendar/callback";
                var scopes = "offline_access Calendars.ReadWrite";
                url = $"https://login.microsoftonline.com/common/oauth2/v2.0/authorize?client_id={clientId}&response_type=code&redirect_uri={redirectUri}&response_mode=query&scope=offline_access%20Calendars.ReadWrite&state={userChat.Code}";
            }
            else
            {
                return BadRequest(new { error = "Unknown provider" });
            }
            return Ok(new { url });
        }

        [HttpDelete("user/{userId}/chat/{chatCode}")]
        public async Task<IActionResult> DeleteChat(int userId, string chatCode)
        {
            try
            {
                var success = await _chatService.DeleteChatAsync(userId, chatCode);
                if (success)
                {
                    return Ok(new { message = "Chat deleted successfully" });
                }
                else
                {
                    return NotFound(new { message = "Chat not found or not authorized to delete" });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"Error deleting chat: {ex.Message}" });
            }
        }
    }
}
