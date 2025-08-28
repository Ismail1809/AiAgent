using AiAgent.MobileServiceApi.Data;
using AiAgent.MobileServiceApi.Models;
using Microsoft.EntityFrameworkCore;

namespace AiAgent.MobileServiceApi.Services
{
    public class MessageService
    {
        private readonly ApplicationDbContext _context;

        public MessageService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<List<ChatMessage>> GetChatMessagesAsync(int chatId)
        {
            return await _context.ChatMessages
                .Where(m => m.ChatId == chatId)
                .OrderBy(m => m.CreatedAt)
                .ToListAsync();
        }

        public async Task<List<ChatMessage>> GetChatMessagesByCodeAsync(string chatCode)
        {
            var chat = await _context.Chats
                .FirstOrDefaultAsync(c => c.Code == chatCode);

            if (chat == null)
                return new List<ChatMessage>();

            return await GetChatMessagesAsync(chat.Id);
        }

        public async Task<ChatMessage> AddMessageAsync(int chatId, string sender, string content)
        {
            // Ensure only last 10 messages are kept
            var messages = await _context.ChatMessages
                .Where(m => m.ChatId == chatId)
                .OrderBy(m => m.CreatedAt)
                .ToListAsync();
            if (messages.Count >= 10)
            {
                var toRemove = messages.Take(messages.Count - 9).ToList();
                _context.ChatMessages.RemoveRange(toRemove);
                await _context.SaveChangesAsync();
            }

            var message = new ChatMessage
            {
                ChatId = chatId,
                Sender = sender,
                Content = content,
                CreatedAt = DateTime.UtcNow
            };

            _context.ChatMessages.Add(message);
            await _context.SaveChangesAsync();

            return message;
        }

        public async Task<ChatMessage> AddMessageByCodeAsync(string chatCode, string sender, string content)
        {
            var chat = await _context.Chats
                .FirstOrDefaultAsync(c => c.Code == chatCode);

            if (chat == null)
                throw new InvalidOperationException($"Chat with code {chatCode} not found");

            return await AddMessageAsync(chat.Id, sender, content);
        }

        public async Task<List<string>> GetChatHistoryAsync(string chatCode)
        {
            var messages = await GetChatMessagesByCodeAsync(chatCode);
            return messages.Select(m => $"{m.Sender}: {m.Content}").ToList();
        }

        public async Task ClearChatMessagesAsync(int chatId)
        {
            var messages = await _context.ChatMessages
                .Where(m => m.ChatId == chatId)
                .ToListAsync();

            _context.ChatMessages.RemoveRange(messages);
            await _context.SaveChangesAsync();
        }

        public async Task ClearChatMessagesByCodeAsync(string chatCode)
        {
            var chat = await _context.Chats
                .FirstOrDefaultAsync(c => c.Code == chatCode);

            if (chat != null)
            {
                await ClearChatMessagesAsync(chat.Id);
            }
        }
    }
}

