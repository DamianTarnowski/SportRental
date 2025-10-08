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

        [Required]
        [MaxLength(10)]
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
