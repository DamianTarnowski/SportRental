using System.ComponentModel.DataAnnotations;

namespace SportRental.Infrastructure.Domain
{
    /// <summary>
    /// Konwersacja użytkownika z asystentem — sesja rozmowy w obrębie jednego "circuit"
    /// Blazor Server (lub voice WebRTC). Persystowane między sesjami żeby model miał
    /// long-term memory o czym z tym konkretnym użytkownikiem rozmawialiśmy.
    /// </summary>
    public class ChatConversation
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid TenantId { get; set; }

        [Required]
        public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime? EndedAtUtc { get; set; }

        public string? UserId { get; set; }

        [MaxLength(256)]
        public string? UserEmail { get; set; }

        [MaxLength(64)]
        public string? UserRole { get; set; }

        /// <summary>"text" / "voice" — żeby odróżnić w analizie.</summary>
        [MaxLength(16)]
        public string Source { get; set; } = "text";

        public int MessageCount { get; set; }

        // Navigation
        public Tenant? Tenant { get; set; }
        public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
    }

    public class ChatMessage
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid ConversationId { get; set; }

        public Guid TenantId { get; set; }

        /// <summary>"user" lub "assistant".</summary>
        [Required, MaxLength(16)]
        public string Role { get; set; } = "user";

        [Required]
        public string Content { get; set; } = string.Empty;

        [Required]
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        /// <summary>JSONB z tool calls (opcjonalne — gdy assistant wywołał tools).</summary>
        public string? ToolCallsJson { get; set; }

        /// <summary>Strona z której user pisał (URL).</summary>
        [MaxLength(512)]
        public string? CurrentPage { get; set; }

        // Navigation
        public ChatConversation? Conversation { get; set; }
    }
}
