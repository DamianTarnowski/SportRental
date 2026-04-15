using System.ComponentModel.DataAnnotations;

namespace SportRental.Infrastructure.Domain
{
    /// <summary>
    /// Potwierdzenie wynajmu przez klienta — klient klika link, akceptuje regulamin, potwierdza wynajem.
    /// Przechowuje dowód prawny akceptacji (IP, user-agent, hash regulaminu).
    /// </summary>
    public class RentalConfirmation
    {
        [Key]
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid RentalId { get; set; }

        /// <summary>
        /// Unikalny token URL-safe, używany w linku potwierdzającym
        /// </summary>
        [Required]
        [MaxLength(128)]
        public string Token { get; set; } = string.Empty;

        [MaxLength(20)]
        public string? PhoneNumber { get; set; }

        [MaxLength(256)]
        public string? Email { get; set; }

        // Confirmation data
        public bool IsConfirmed { get; set; } = false;
        public DateTime? ConfirmedAt { get; set; }

        [MaxLength(45)]
        public string? ConfirmedFromIp { get; set; }

        [MaxLength(500)]
        public string? ConfirmedUserAgent { get; set; }

        /// <summary>
        /// SHA256 hash regulaminu w momencie akceptacji — dowód jaki regulamin został zaakceptowany
        /// </summary>
        [MaxLength(64)]
        public string? RegulationsHash { get; set; }

        // Timestamps
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddHours(48);

        // Link tracking
        public bool IsSmsSent { get; set; } = false;
        public bool IsEmailSent { get; set; } = false;

        // Navigation
        public Rental? Rental { get; set; }
        public Tenant? Tenant { get; set; }
    }
}
