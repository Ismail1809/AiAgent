using AiAgent.ApiService.Components;
using Microsoft.AspNetCore.Mvc;

namespace AiAgent.ApiService.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TelegramNotificationController : ControllerBase
    {

        TelegramComponent _telegramComponent = new TelegramComponent();

        [HttpPost("SendMessage")]
        public async void SendMessage(string message)
        {
            _telegramComponent.SendMessage(message);
        }
    }
}
