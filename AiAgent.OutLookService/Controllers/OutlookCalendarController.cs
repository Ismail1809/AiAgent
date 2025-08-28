using Microsoft.AspNetCore.Mvc;
using Microsoft.Graph;
using Microsoft.Identity.Client;
using Microsoft.Extensions.Configuration;
using System;
using System.Threading.Tasks;
using System.Net.Http.Headers;
using System.Collections.Generic;
using System.Net.Mail;
using Microsoft.Graph.Models;
using Microsoft.Graph.Authentication;
using Azure.Identity;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Kiota.Abstractions.Authentication;
using static System.Net.WebRequestMethods;
using System.ComponentModel.DataAnnotations;
using AiAgent.MobileServiceApi.Data;
using Microsoft.EntityFrameworkCore;

namespace AiAgent.OutlookCalendarService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class OutlookCalendarController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly IConfidentialClientApplication _msalClient;
        private readonly ApplicationDbContext _context;
        public OutlookCalendarController(IConfiguration config, ApplicationDbContext context)
        {
            _config = config;
            _msalClient = ConfidentialClientApplicationBuilder
                .Create("636bc4b3-9e68-4fcc-9ab5-2750ff8bf135")
                .WithClientSecret("eRd8Q~jdjll3I-vl-H4Mj8ibSZSLguJ9UP1Y5dqS")
                .WithAuthority(new Uri($"https://login.microsoftonline.com/common"))
                .Build();
            _context = context;
        }

        [HttpGet("auth-link")]
        public IActionResult GetAuthLink()
        {
            var clientId = "636bc4b3-9e68-4fcc-9ab5-2750ff8bf135";
            var redirectUri = "https://bacbba3bc43f.ngrok-free.app/api/OutlookCalendar/callback";
            var scopes = "offline_access Calendars.ReadWrite";
            var url = $"https://login.microsoftonline.com/common/oauth2/v2.0/authorize?client_id={clientId}&response_type=code&redirect_uri={Uri.EscapeDataString(redirectUri)}&response_mode=query&scope={Uri.EscapeDataString(scopes)}";
            return Ok(new { url });
        }

        // 2. OAuth2 callback endpoint (returns refresh token to user for demo)
        [HttpGet("callback")]
        public async Task<IActionResult> Callback([FromQuery] string code, [FromQuery] string state)
        {
            var clientId = "636bc4b3-9e68-4fcc-9ab5-2750ff8bf135";
            var clientSecret = "eRd8Q~jdjll3I-vl-H4Mj8ibSZSLguJ9UP1Y5dqS";
            var redirectUri = "https://bacbba3bc43f.ngrok-free.app/api/OutlookCalendar/callback";
            var tokenEndpoint = "https://login.microsoftonline.com/common/oauth2/v2.0/token";

            using var httpClient = new HttpClient();
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("client_id", clientId),
                new KeyValuePair<string, string>("scope", "offline_access Calendars.ReadWrite"),
                new KeyValuePair<string, string>("code", code),
                new KeyValuePair<string, string>("redirect_uri", redirectUri),
                new KeyValuePair<string, string>("grant_type", "authorization_code"),
                new KeyValuePair<string, string>("client_secret", clientSecret)
            });

            var response = await httpClient.PostAsync(tokenEndpoint, content);
            var responseString = await response.Content.ReadAsStringAsync();

            // Parse the refresh_token from the response
            var json = System.Text.Json.JsonDocument.Parse(responseString);
            var refreshToken = json.RootElement.GetProperty("refresh_token").GetString();

            // POST the refresh token to the bot's webhook endpoint
            var chat = await _context.Chats.FirstOrDefaultAsync(c => c.Code == state);
            if (chat != null)
            {
                var userChat = await _context.UserChats
                    .Include(uc => uc.User)
                    .FirstOrDefaultAsync(uc => uc.ChatId == chat.Id);

                if (userChat?.User != null)
                {
                    userChat.User.OutlookRefreshToken = refreshToken;
                    await _context.SaveChangesAsync();
                }
            }

            // Return a simple message to the user
            return Content("<html><body style='display:flex;align-items:center;justify-content:center;height:100vh;'><h2>You can now close the page and continue the dialogue.</h2></body></html>", "text/html");
        }

        [HttpPost("schedule")]
        public async Task<IActionResult> ScheduleMeeting([FromQuery][Required] string refreshToken, [FromBody] CalendarEventRequest request, CancellationToken cancellationToken)
        {

            var clientId = "636bc4b3-9e68-4fcc-9ab5-2750ff8bf135";
            var clientSecret = "eRd8Q~jdjll3I-vl-H4Mj8ibSZSLguJ9UP1Y5dqS";
            var tenantId = "9a3b887a-0ea7-40e9-b940-da24c82ee38b";
            //var refreshToken = "M.C536_SN1.0.U.-CnEqfGbG4IU!4AAGau2MXmy8VAL47MjOd!Ci1JDlOLHT4ZqTcH9XU9EP5q4Hv671*DyQDdxmtZqRGu43tUb4MPbRVNOXCYoTJ6ik8Txs9rWHqaf1fzcn7wg1ugVXCnvkSSRIcrxnFqH8F7DhZs*cbEN0x9GVVPITEAt7zB8xgSnJi2rAYmAO2RfWN1sHRS7NHD4VP2w3kcAh3C9Agujwq6I61GscTi!4sJkA7qPWq3EAF1GU14YQO5Qi!chWLxSuQSRVP0qGw0zW7dG8kutkVV4JhnBTW5BlMvC2fEY7fmY8lEte!CIm4VUZGcUrlwuNZCBJsJohBTZJnLpOsXtYzEZ*XiUduz3jDmsfjzi!0gOTv2xZoJQ*BhIMAr0XkOaojw$$";
            var redirectUri = "http://localhost:5046/api/OutlookCalendar/callback";

            var tokenEndpoint = "https://login.microsoftonline.com/common/oauth2/v2.0/token";
            using var httpClient = new HttpClient();
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("client_id", clientId),
                new KeyValuePair<string, string>("scope", "offline_access Calendars.ReadWrite"),
                new KeyValuePair<string, string>("refresh_token", refreshToken),
                new KeyValuePair<string, string>("redirect_uri", redirectUri),
                new KeyValuePair<string, string>("grant_type", "refresh_token"),
                new KeyValuePair<string, string>("client_secret", clientSecret)
            });

            var response = await httpClient.PostAsync(tokenEndpoint, content, cancellationToken);
            var responseString = await response.Content.ReadAsStringAsync(cancellationToken);
            var json = System.Text.Json.JsonDocument.Parse(responseString);
            var accessToken = json.RootElement.GetProperty("access_token").GetString();

            var accessTokenProvider = new StaticAccessTokenProvider(accessToken);
            var authProvider = new BaseBearerTokenAuthenticationProvider(accessTokenProvider);
            var graphClient = new GraphServiceClient(authProvider); 


            var @event = new Event
            {
                Subject = request.Summary,
                Body = new ItemBody { ContentType = BodyType.Text, Content = request.Description },
                Start = new DateTimeTimeZone
                {
                    DateTime = request.Start?.ToString("yyyy-MM-ddTHH:mm:ss"),
                    TimeZone = request.TimeZone ?? "UTC"
                },
                End = new DateTimeTimeZone
                {
                    DateTime = request.End?.ToString("yyyy-MM-ddTHH:mm:ss"),
                    TimeZone = request.TimeZone ?? "UTC"
                },
                Attendees = request.Attendees != null ? new List<Attendee>(
                    Array.ConvertAll(request.Attendees, email => new Attendee
                    {
                        EmailAddress = new EmailAddress { Address = email },
                        Type = AttendeeType.Required
                    })) : null
            };

            try
            {
                var createdEvent = await graphClient.Me.Events.PostAsync(@event);
                return Ok(new { id = createdEvent.Id, htmlLink = createdEvent.WebLink });
            }
            catch (ODataError ex)
            {
                return StatusCode(500, ex.Error?.Message);
            }

        }
    }

    public class StaticAccessTokenProvider : IAccessTokenProvider
    {
        private readonly string _accessToken;

        public StaticAccessTokenProvider(string accessToken)
        {
            _accessToken = accessToken;
        }

        public AllowedHostsValidator AllowedHostsValidator { get; } = new AllowedHostsValidator();

        public Task<string> GetAuthorizationTokenAsync(Uri uri, Dictionary<string, object> additionalAuthenticationContext = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_accessToken);
        }
    }

    public class CalendarEventRequest
    {
        public string Summary { get; set; }
        public string Description { get; set; }
        public DateTime? Start { get; set; }
        public DateTime? End { get; set; }
        public string TimeZone { get; set; } = "UTC";
        public string[]? Attendees { get; set; }
    }
}