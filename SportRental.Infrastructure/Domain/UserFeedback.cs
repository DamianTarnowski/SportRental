using System.ComponentModel.DataAnnotations;

namespace SportRental.Infrastructure.Domain
{
    /// <summary>
    /// Zgłoszenie od użytkownika z floating chat — błąd, sugestia, ogólny feedback.
    /// Tworzone albo bezpośrednio przez UI, albo przez tool call asystenta AI.
    /// SuperAdmin widzi wszystko cross-tenant; Owner — tylko wpisy z TenantId == swój.
    /// </summary>
    public class UserFeedback
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid TenantId { get; set; }

        [Required]
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public string? UserId { get; set; }

        [MaxLength(256)]
        public string? UserEmail { get; set; }

        [MaxLength(64)]
        public string? UserRole { get; set; }

        /// <summary>Ścieżka strony, np. "/admin/rentals" — z NavigationManager.Uri.</summary>
        [MaxLength(512)]
        public string? CurrentPage { get; set; }

        [Required]
        public FeedbackType Type { get; set; } = FeedbackType.General;

        [Required]
        [MaxLength(8000)]
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// Dowolny kontekst — historia ostatnich kilku wiadomości chat, browser info, screenshot ref itp.
        /// Storowane jako JSONB.
        /// </summary>
        public string? ContextJson { get; set; }

        public bool IsResolved { get; set; }

        public DateTime? ResolvedAtUtc { get; set; }

        [MaxLength(256)]
        public string? ResolvedBy { get; set; }

        [MaxLength(2000)]
        public string? ResolutionNotes { get; set; }

        // Navigation
        public Tenant? Tenant { get; set; }
    }

    public enum FeedbackType : short
    {
        General = 0,
        Bug = 1,
        Suggestion = 2,
        Question = 3,
        Praise = 4
    }
}
