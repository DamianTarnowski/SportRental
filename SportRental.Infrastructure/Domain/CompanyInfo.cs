using System.ComponentModel.DataAnnotations;

namespace SportRental.Infrastructure.Domain
{
    public class CompanyInfo
    {
        [Key]
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }

        [MaxLength(200)]
        public string? Name { get; set; }

        [MaxLength(300)]
        public string? Address { get; set; }

        [MaxLength(20)]
        public string? NIP { get; set; }

        [MaxLength(14)]
        public string? REGON { get; set; }

        [MaxLength(100)]
        public string? LegalForm { get; set; } = string.Empty;

        [EmailAddress]
        [MaxLength(200)]
        public string? Email { get; set; }

        [Phone]
        [MaxLength(20)]
        public string? PhoneNumber { get; set; }

        [MaxLength(500)]
        public string? OpeningHours { get; set; }

        [MaxLength(1000)]
        public string? Description { get; set; }

        [MaxLength(1000)]
        public string? ExtraInfo { get; set; }

        public Guid? IdAdmin { get; set; }

        [EmailAddress]
        [MaxLength(200)]
        public string? AdminEmail { get; set; }

        [MaxLength(100)]
        public string? AdminName { get; set; }

        // SMS Templates
        [MaxLength(500)]
        public string? SmsThanksText { get; set; } = null;

        [MaxLength(500)]
        public string? SmsReminderText { get; set; } = null;

        [MaxLength(500)]
        public string? SmsReminderText2 { get; set; } = null;

        [MaxLength(500)]
        public string? SmsReminderText3 { get; set; } = null;

        // Email Templates
        [MaxLength(1000)]
        public string? EmailThanksText { get; set; } = null;

        [MaxLength(1000)]
        public string? EmailReminderText { get; set; } = null;

        [MaxLength(1000)]
        public string? EmailReminderText2 { get; set; } = null;

        [MaxLength(1000)]
        public string? EmailReminderText3 { get; set; } = null;

        // GPS Coordinates
        public double? Lat { get; set; } = 0;
        public double? Lon { get; set; } = 0;

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAtUtc { get; set; }

        // Navigation properties
        public Tenant? Tenant { get; set; }
    }
}
