using System.ComponentModel.DataAnnotations;

namespace AiAgent.MobileServiceApi.Models
{
    public class UserChat
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int UserId { get; set; }

        [Required]
        public int ChatId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public virtual User User { get; set; } = null!;
        public virtual Chat Chat { get; set; } = null!;
    }
}
