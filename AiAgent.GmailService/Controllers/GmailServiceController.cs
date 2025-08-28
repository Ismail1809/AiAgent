using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AiAgent.GmailService.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class GmailServiceController : ControllerBase
    {
        private readonly GmailServiceAgent _gmailService;
        public GmailServiceController(GmailServiceAgent gmailService)
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
