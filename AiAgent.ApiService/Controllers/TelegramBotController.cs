using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AiAgent.ApiService.Controllers
{
    [ApiController]
    [Route("api/telegrambot")]
    public class TelegramBotController : ControllerBase
    {
        private readonly TelegramBotPollingService _botService;
        public TelegramBotController(TelegramBotPollingService botService)
        {
            _botService = botService;
        }

        [HttpPost("outlooktoken")]
        public IActionResult SaveOutlookToken([FromBody] OutlookTokenDto dto)
        {
            if (dto == null || string.IsNullOrEmpty(dto.RefreshToken) || dto.ChatId == 0)
                return BadRequest();

            TelegramBotPollingService.OutlookRefreshTokens[dto.ChatId] = dto.RefreshToken;
            return Ok();
        }

        [HttpPost("googletoken")]
        public IActionResult SaveGoogleToken([FromBody] GoogleTokenDto dto)
        {
            if (dto == null || string.IsNullOrEmpty(dto.RefreshToken) || dto.ChatId == 0)
                return BadRequest();

            TelegramBotPollingService.GoogleRefreshTokens[dto.ChatId] = dto.RefreshToken;
            return Ok();
        }

        [HttpPost("escalate")]
        public async Task<IActionResult> Escalate([FromBody] EscalationRequest request)
        {
            // Передаем эскалированное письмо в TelegramBotPollingService для отправки пользователю
            await _botService.SendEscalationToUserAsync(request.ChatId, request.SenderEmail, request.Subject, request.Body, request.SuggestedReply);
            return Ok();
        }

        [HttpGet("google-tokens")]
        public ActionResult<Dictionary<long, string>> GetGoogleTokens()
        {
            return Ok(TelegramBotPollingService.GoogleRefreshTokens);
        }

        [HttpGet("outlook-tokens")]
        public ActionResult<Dictionary<long, string>> GetOutLookTokens()
        {
            return Ok(TelegramBotPollingService.OutlookRefreshTokens);
        }

        [HttpGet("google-token/{chatId}")]
        public ActionResult<string> GetGoogleToken(long chatId)
        {
            if (TelegramBotPollingService.GoogleRefreshTokens.TryGetValue(chatId, out var token))
                return Ok(token);
            return NotFound();
        }

        [HttpPost("google-token")]
        public IActionResult SetGoogleToken([FromBody] GoogleTokenDto dto)
        {
            TelegramBotPollingService.GoogleRefreshTokens[dto.ChatId] = dto.RefreshToken;
            return Ok();
        }

        public class GoogleTokenDto
        {
            public long ChatId { get; set; }
            public string RefreshToken { get; set; }
        }

        public class OutlookTokenDto
        {
            public long ChatId { get; set; }
            public string RefreshToken { get; set; }
        }

        public class EscalationRequest
        {
            public long ChatId { get; set; }
            public string SenderEmail { get; set; }
            public string Subject { get; set; }
            public string Body { get; set; }
            public string SuggestedReply { get; set; }
        }
    }
}
