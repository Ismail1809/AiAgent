using System.ComponentModel.DataAnnotations;

namespace AiAgent.MobileServiceApi.Models
{
    public class ChatMessage
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int ChatId { get; set; }

        [Required]
        [StringLength(50)]
        public string Sender { get; set; } = string.Empty; // "User" or "AI"

        [Required]
        public string Content { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation property
        public virtual Chat Chat { get; set; } = null!;
    }
}
