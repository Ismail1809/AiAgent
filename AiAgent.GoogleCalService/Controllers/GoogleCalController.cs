using AiAgent.MobileServiceApi.Data;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System;
using System.ComponentModel.DataAnnotations;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace AiAgent.GoogleCalendarService.Controllers
{
    public class GoogleCalController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly ApplicationDbContext _context;

        public GoogleCalController(IConfiguration config, ApplicationDbContext context)
        {
            _config = config;
            _context = context;
        }

        [HttpPost("schedule")]
        public async Task<IActionResult> ScheduleMeeting([FromQuery][Required] string refreshToken, [FromBody] CalendarEventRequest request, CancellationToken cancellationToken)
        {
            var clientId = "*";
            var clientSecret = "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";
            var calendarId = _config["CalendarId"] ?? "primary";
            var user = _config["GoogleOAuthUser"] ?? "user";

            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret) || string.IsNullOrEmpty(refreshToken))
                return BadRequest("Google API credentials or refresh token are not configured.");

            var token = new Google.Apis.Auth.OAuth2.Responses.TokenResponse { RefreshToken = refreshToken };
            var secrets = new Google.Apis.Auth.OAuth2.ClientSecrets { ClientId = clientId, ClientSecret = clientSecret };
            var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = secrets
            });
            var credential = new UserCredential(flow, user, token);
            if (credential.Token.IsExpired(Google.Apis.Util.SystemClock.Default) || string.IsNullOrEmpty(credential.Token.AccessToken))
            {
                var newToken = await flow.RefreshTokenAsync(user, refreshToken, cancellationToken);
                credential.Token.AccessToken = newToken.AccessToken;
                credential.Token.IssuedUtc = newToken.IssuedUtc;
                credential.Token.ExpiresInSeconds = newToken.ExpiresInSeconds;
                credential.Token.TokenType = newToken.TokenType;
                // Optionally update the refresh token if it changes
                if (!string.IsNullOrEmpty(newToken.RefreshToken))
                    credential.Token.RefreshToken = newToken.RefreshToken;
            }

            var service = new CalendarService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "AiAgent.GoogleCalendarService"
            });

            var newEvent = new Event
            {
                Summary = request.Summary,
                Description = request.Description,
                Start = new EventDateTime { DateTime = request.Start, TimeZone = request.TimeZone },
                End = new EventDateTime { DateTime = request.End, TimeZone = request.TimeZone },
                Attendees = request.Attendees != null ? Array.ConvertAll(request.Attendees, email => new EventAttendee { Email = email }) : null
            };

            var insertRequest = service.Events.Insert(newEvent, calendarId);
            var createdEvent = await insertRequest.ExecuteAsync(cancellationToken);

            return Ok(new { createdEvent.Id, createdEvent.HtmlLink });
        }

        [HttpGet("authorize")]
        public IActionResult Authorize()
        {
            var clientId = "*";
            var redirectUri = "https://39e9bcf6cbed.ngrok-free.app/oauth/callback";
            var scopes = new[]
            {
                "https://mail.google.com/",
                "https://www.googleapis.com/auth/calendar.events"
            };

            // Объединяем scopes в одну строку, разделяя пробелом
            var scopeParam = string.Join(" ", scopes);

            var url = $"https://accounts.google.com/o/oauth2/v2/auth?response_type=code" +
                      $"&client_id={clientId}" +
                      $"&redirect_uri={HttpUtility.UrlEncode(redirectUri)}" +
                      $"&scope={HttpUtility.UrlEncode(scopeParam)}" +
                      $"&access_type=offline&prompt=consent"; 
            return Redirect(url);
        }

        [HttpGet("oauth/callback")]
        public async Task<IActionResult> Callback([FromQuery] string code, [FromQuery] string state)
        {
            var clientId = "*";
            var clientSecret = "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";
            var redirectUri = "https://39e9bcf6cbed.ngrok-free.app/oauth/callback";
            if (string.IsNullOrEmpty(code))
                return BadRequest("No code provided");

            var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = new ClientSecrets
                {
                    ClientId = clientId,
                    ClientSecret = clientSecret
                }
            });

            var token = await flow.ExchangeCodeForTokenAsync(
                userId: "user",
                code: code,
                redirectUri: redirectUri,
                taskCancellationToken: CancellationToken.None);

            using var httpClient = new HttpClient();

            var chat = await _context.Chats.FirstOrDefaultAsync(c => c.Code == state);
            if (chat != null)
            {
                var userChat = await _context.UserChats
                    .Include(uc => uc.User)
                    .FirstOrDefaultAsync(uc => uc.ChatId == chat.Id);

                if (userChat?.User != null)
                {
                    userChat.User.GoogleRefreshToken = token.RefreshToken;
                    await _context.SaveChangesAsync();
                }
            }

            // Show tokens in browser for copy-paste
            return Content("<html><body style='display:flex;align-items:center;justify-content:center;height:100vh;'><h2>You can now close the page and continue the dialogue.</h2></body></html>", "text/html");
        }
    }

    public class CalendarEventRequest
    {
        public string Summary { get; set; }
        public string Description { get; set; }
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public string TimeZone { get; set; } = "UTC";
        public string[]? Attendees { get; set; }
    }
}