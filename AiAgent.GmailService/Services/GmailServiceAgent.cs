using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Services;
using Google.Apis.Util;
using MimeKit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

public class GmailServiceAgent
{
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly string _applicationName;

    public GmailServiceAgent(string clientId, string clientSecret, string applicationName = "AiAgent Gmail Integration")
    {
        _clientId = clientId;
        _clientSecret = clientSecret;
        _applicationName = applicationName;
    }

    // Получить GmailService по refresh token
    public async Task<GmailService> GetGmailServiceAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var token = new Google.Apis.Auth.OAuth2.Responses.TokenResponse
        {
            RefreshToken = refreshToken
        };

        var credentials = new UserCredential(
            new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = new ClientSecrets
                {
                    ClientId = _clientId,
                    ClientSecret = _clientSecret
                }
            }),
            "user",
            token);

        // Принудительно обновить access_token, если нужно
        if (credentials.Token.IsExpired(SystemClock.Default))
        {
            await credentials.RefreshTokenAsync(cancellationToken);
        }

        return new GmailService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credentials,
            ApplicationName = _applicationName
        });
    }

    // Получить последние письма
    public async Task<IList<GmailMessageWithSender>> GetRecentMessagesAsync(string refreshToken, int maxResults = 1, CancellationToken cancellationToken = default)
    {
        var service = await GetGmailServiceAsync(refreshToken, cancellationToken);
        var request = service.Users.Messages.List("me");
        request.Q = "is:unread"; // Only fetch unread messages
        request.MaxResults = maxResults;
        var response = await request.ExecuteAsync(cancellationToken);
        var messages = response.Messages ?? new List<Message>();

        var result = new List<GmailMessageWithSender>();
        foreach (var msg in messages)
        {
            var fullMsg = await service.Users.Messages.Get("me", msg.Id).ExecuteAsync(cancellationToken);
            var fromHeader = fullMsg.Payload?.Headers?.FirstOrDefault(h => h.Name.Equals("From", StringComparison.OrdinalIgnoreCase));
            string senderEmail = null;
            if (fromHeader != null)
            {
                try
                {
                    var mailbox = MimeKit.MailboxAddress.Parse(fromHeader.Value);
                    senderEmail = mailbox.Address;
                }
                catch
                {
                    senderEmail = fromHeader.Value;
                }
            }
            result.Add(new GmailMessageWithSender { Message = fullMsg, SenderEmail = senderEmail });

            // Optionally, mark messages as read after fetching
            var modifyRequest = new Google.Apis.Gmail.v1.Data.ModifyMessageRequest
            {
                RemoveLabelIds = new List<string> { "UNREAD" }
            };
            await service.Users.Messages.Modify(modifyRequest, "me", msg.Id).ExecuteAsync(cancellationToken);
        }
        return result;
    }

    // Получить тело письма
    public async Task<string> GetMessageBodyAsync(string refreshToken, string messageId, CancellationToken cancellationToken = default)
    {
        var service = await GetGmailServiceAsync(refreshToken, cancellationToken);
        var request = service.Users.Messages.Get("me", messageId);
        var message = await request.ExecuteAsync(cancellationToken);
        var parts = message.Payload?.Parts;
        if (parts != null)
        {
            foreach (var part in parts)
            {
                if (part.MimeType == "text/plain" && part.Body?.Data != null)
                {
                    return Base64UrlDecode(part.Body.Data);
                }
            }
        }
        // Если письмо не multipart
        if (message.Payload?.Body?.Data != null)
        {
            return Base64UrlDecode(message.Payload.Body.Data);
        }
        return string.Empty;
    }

    public async Task<string?> GetMostRecentMessageIdAsync(string refreshToken, CancellationToken ct)
    {
        var service = await GetGmailServiceAsync(refreshToken, ct);
        var request = service.Users.Messages.List("me");
        request.LabelIds = new[] { "INBOX" };
        request.MaxResults = 1;

        var response = await request.ExecuteAsync(ct);
        return response.Messages?.FirstOrDefault()?.Id;
    }

    // Отправить письмо
    public async Task SendMessageAsync(string refreshToken, string to, string subject, string body, CancellationToken cancellationToken = default)
    {
        var service = await GetGmailServiceAsync(refreshToken, cancellationToken);

        // Собираем MIME-письмо с помощью MimeKit
        var message = new MimeKit.MimeMessage(); // укажи свою почту
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;

        message.Body = new TextPart("plain")
        {
            Text = body,
        };

        // Преобразуем MIME-сообщение в байты
        using var stream = new MemoryStream();
        await message.WriteToAsync(stream, cancellationToken);
        var rawMessage = Convert.ToBase64String(stream.ToArray())
            .Replace('+', '-')
            .Replace('/', '_')
            .Replace("=", "");

        // Оборачиваем в Gmail API объект
        var gmailMessage = new Message
        {
            Raw = rawMessage
        };

        // Отправляем сообщение от имени пользователя
        await service.Users.Messages.Send(gmailMessage, "me").ExecuteAsync(cancellationToken);
    }



    private static string Base64UrlDecode(string input)
    {
        string s = input.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        var bytes = Convert.FromBase64String(s);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    public class GmailMessageWithSender
    {
        public Message Message { get; set; }
        public string SenderEmail { get; set; }
    }
}