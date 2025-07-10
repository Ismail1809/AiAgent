using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;
using System.Threading;
using System;
using System.Web;
using Google.Apis.Auth.OAuth2.Flows;

namespace AiAgent.GoogleCalendarService.Controllers
{
    public class GoogleCalController : ControllerBase
    {
        private readonly IConfiguration _config;

        public GoogleCalController(IConfiguration config)
        {
            _config = config;
        }

        [HttpPost("schedule")]
        public async Task<IActionResult> ScheduleMeeting([FromBody] CalendarEventRequest request, CancellationToken cancellationToken)
        {
            var clientId = "722351317793-pvvn7tgiah8ik3oajucug6oq89t6gbcg.apps.googleusercontent.com";
            var clientSecret = "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";
            var calendarId = _config["CalendarId"] ?? "primary";
            var refreshToken = "1//03dxm_yBi6mH3CgYIARAAGAMSNwF-L9IrruPL4vOHh0qyNCxQehb93ofE8ntCwKq9lClqX8YUNi74iC4FSy2UTawpVNsIUS-D1-Y";
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
            if (credential.Token.IsExpired(Google.Apis.Util.SystemClock.Default))
            {
                await credential.RefreshTokenAsync(cancellationToken);
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
            var clientId = "722351317793-pvvn7tgiah8ik3oajucug6oq89t6gbcg.apps.googleusercontent.com";
            var redirectUri = "https://localhost:7262/oauth/callback";
            var scope = "https://www.googleapis.com/auth/calendar.events";
            var url = $"https://accounts.google.com/o/oauth2/v2/auth?response_type=code&client_id={clientId}&redirect_uri={HttpUtility.UrlEncode(redirectUri)}&scope={HttpUtility.UrlEncode(scope)}&access_type=offline&prompt=consent";
            return Redirect(url);
        }

        [HttpGet("oauth/callback")]
        public async Task<IActionResult> Callback([FromQuery] string code)
        {
            var clientId = "722351317793-pvvn7tgiah8ik3oajucug6oq89t6gbcg.apps.googleusercontent.com";
            var clientSecret = "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d";
            var redirectUri = _config["GoogleOAuthRedirectUri"] ?? "https://localhost:7262/oauth/callback";
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

            // Show tokens in browser for copy-paste
            return Content($@"
            <h2>Google OAuth2 Tokens</h2>
            <b>Access Token:</b> <pre>{token.AccessToken}</pre>
            <b>Refresh Token:</b> <pre>{token.RefreshToken}</pre>
            <b>Expires In:</b> {token.ExpiresInSeconds} seconds
            <br><br>
            <b>Copy the refresh token and use it in your configuration!</b>
        ", "text/html");
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