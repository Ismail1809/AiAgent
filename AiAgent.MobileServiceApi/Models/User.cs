using System.ComponentModel.DataAnnotations;

namespace AiAgent.MobileServiceApi.Models
{
    public class User
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        public string Username { get; set; } = string.Empty;

        [StringLength(255)]
        public string? Email { get; set; }

        [StringLength(500)]
        public string? PasswordHash { get; set; }

        [StringLength(500)]
        public string? GoogleRefreshToken { get; set; }

        [StringLength(500)]
        public string? OutlookRefreshToken { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public virtual ICollection<UserChat> UserChats { get; set; } = new List<UserChat>();
    }
}
