using System.ComponentModel.DataAnnotations;

namespace SportRental.Infrastructure.Domain
{
    public class SmsConfirmation
    {
        [Key]
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }

        [Required]
        public Guid RentalId { get; set; }

        // SEC-012: trzymamy SHA-256(Id + plaintextCode) w base64 (~44 znaki) zamiast plaintext 6-cyfr.
        // Stara MaxLength=10 (plaintext) zostala podniesiona do 128 dla hash + ewentualne legacy.
        // Walidacja przez FixedTimeEquals — patrz SmsConfirmationService.
        [Required]
        [MaxLength(128)]
        public string Code { get; set; } = string.Empty;

        [Required]
        [Phone]
        [MaxLength(20)]
        public string PhoneNumber { get; set; } = string.Empty;

        public bool IsConfirmed { get; set; } = false;
        public DateTime? ConfirmedAt { get; set; }
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddHours(24);

        public int AttemptsCount { get; set; } = 0;
        public DateTime? LastAttemptAt { get; set; }

        // Navigation properties
        public Tenant? Tenant { get; set; }
        public Rental? Rental { get; set; }
    }
}
