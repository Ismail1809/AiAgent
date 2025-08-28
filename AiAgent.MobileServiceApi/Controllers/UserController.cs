using AiAgent.MobileServiceApi.Data;
using AiAgent.MobileServiceApi.Models;
using AiAgent.MobileServiceApi.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;   
using System.Security.Cryptography;
using System.Text;

namespace AiAgent.MobileServiceApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UserController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ChatApiService _chatService;

        public UserController(ApplicationDbContext context, ChatApiService chatService)
        {
            _context = context;
            _chatService = chatService;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest(new { message = "Username and password are required" });
            }

            // Check if username already exists
            if (await _context.Users.AnyAsync(u => u.Username == request.Username))
            {
                return BadRequest(new { message = "Username already exists" });
            }

            // Hash password
            var passwordHash = HashPassword(request.Password);

            var user = new User
            {
                Username = request.Username,
                Email = request.Email,
                PasswordHash = passwordHash,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            // Find or create chat for user
            var chat = await _chatService.CreateChatForUserAsync(user.Id);

            return Ok(new
            {
                message = "User registered successfully",
                userId = user.Id,
                username = user.Username,
                chatCode = chat.Code
            });
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest(new { message = "Username and password are required" });
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == request.Username);

            if (user == null)
            {
                return Unauthorized(new { message = "Invalid username or password" });
            }

            // Verify password hash
            var inputPasswordHash = HashPassword(request.Password);
            if (user.PasswordHash != inputPasswordHash)
            {
                return Unauthorized(new { message = "Invalid username or password" });
            }

            // Find or create chat for user
            var chat = await _chatService.CreateChatForUserAsync(user.Id);

            return Ok(new
            {
                message = "Login successful",
                userId = user.Id,
                username = user.Username,
                email = user.Email,
                chatCode = chat.Code
            });
        }

        [HttpGet("profile/{userId}")]
        public async Task<IActionResult> GetProfile(int userId)
        {
            var user = await _context.Users.FindAsync(userId);

            if (user == null)
            {
                return NotFound(new { message = "User not found" });
            }

            // Find or create chat for user
            var chat = await _chatService.CreateChatForUserAsync(user.Id);

            return Ok(new
            {
                userId = user.Id,
                username = user.Username,
                email = user.Email,
                createdAt = user.CreatedAt,
                chatCode = chat.Code,
                googleRefreshToken = user.GoogleRefreshToken,
                outlookRefreshToken = user.OutlookRefreshToken
            });
        }

        [HttpPost("oauth/google-callback")]
        public async Task<IActionResult> GoogleOAuthCallback([FromBody] OAuthCallbackRequest request)
        {
            if (string.IsNullOrEmpty(request.ChatCode) || string.IsNullOrEmpty(request.RefreshToken))
            {
                return BadRequest(new { message = "Chat code and refresh token are required" });
            }

            // Find user by chat code
            var chat = await _context.Chats.FirstOrDefaultAsync(c => c.Code == request.ChatCode);
            if (chat == null)
            {
                return NotFound(new { message = "Chat not found" });
            }

            var userChat = await _context.UserChats
                .Include(uc => uc.User)
                .FirstOrDefaultAsync(uc => uc.ChatId == chat.Id);

            if (userChat?.User == null)
            {
                return NotFound(new { message = "User not found" });
            }

            // Update user's Google refresh token
            userChat.User.GoogleRefreshToken = request.RefreshToken;
            userChat.User.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(new { message = "Google account linked successfully" });
        }
        [HttpPost("oauth/outlook-callback")]
        public async Task<IActionResult> OutlookOAuthCallback([FromBody] OAuthCallbackRequest request)
        {
            var user = await _context.Users
                .Include(u => u.UserChats)
                .ThenInclude(uc => uc.Chat)
                .FirstOrDefaultAsync(u => u.UserChats.Any(uc => uc.Chat.Code == request.ChatCode));

            if (user == null)
            {
                return BadRequest(new { message = "User not found for this chat code" });
            }

            user.OutlookRefreshToken = request.RefreshToken;
            await _context.SaveChangesAsync();

            return Ok(new { message = "Outlook account linked successfully" });
        }

        [HttpPost("unlink/google/{userId}")]
        public async Task<IActionResult> UnlinkGoogle(int userId)
        {
            try
            {
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    return NotFound(new { message = $"User not found with ID: {userId}" });
                }

                user.GoogleRefreshToken = null;
                user.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                return Ok(new { message = "Google account unlinked successfully" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"Error unlinking Google account: {ex.Message}" });
            }
        }

        [HttpPost("unlink/outlook/{userId}")]
        public async Task<IActionResult> UnlinkOutlook(int userId)
        {
            try
            {
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    return NotFound(new { message = $"User not found with ID: {userId}" });
                }

                user.OutlookRefreshToken = null;
                user.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                return Ok(new { message = "Outlook account unlinked successfully" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"Error unlinking Outlook account: {ex.Message}" });
            }
        }

        private string HashPassword(string password)
        {
            using var sha256 = SHA256.Create();
            var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
            return Convert.ToBase64String(hashedBytes);
        }
    }

    public class RegisterRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string? Email { get; set; }
    }

    public class LoginRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    public class OAuthCallbackRequest
    {
        public string ChatCode { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
    }
}

