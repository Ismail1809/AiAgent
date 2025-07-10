using Telegram.Bot;

namespace AiAgent.ApiService.Components
{
    public class TelegramComponent
    {
        private readonly string _token = "7990327837:AAENghK8ms3LHGAkXshXzrmgCfQxT5QArEY";
        private readonly string _chatId = "1114074475";
        private TelegramBotClient _client;

        public TelegramComponent()
        {
            _client = new TelegramBotClient(_token);

        }

        public async void SendMessage(string message)
        {
            await _client.SendMessage(_chatId, message);
        }
    }
}
