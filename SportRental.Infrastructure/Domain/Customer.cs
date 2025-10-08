using System.ComponentModel.DataAnnotations;

namespace SportRental.Infrastructure.Domain
{
    public class Customer
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }

        [Required]
        [MaxLength(256)]
        public string FullName { get; set; } = string.Empty;

        [MaxLength(256)]
        [EmailAddress]
        public string? Email { get; set; }

        [MaxLength(32)]
        public string? PhoneNumber { get; set; }

        [MaxLength(64)]
        public string? DocumentNumber { get; set; }

        [MaxLength(512)]
        public string? Address { get; set; }

        [MaxLength(1024)]
        public string? Notes { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}




