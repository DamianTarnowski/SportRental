using System.ComponentModel.DataAnnotations;

namespace SportRental.Infrastructure.Domain
{
    public class AuditLog
    {
        [Key]
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }

        [Required]
        [MaxLength(1000)]
        public string Message { get; set; } = string.Empty;

        [Required]
        public DateTime Date { get; set; } = DateTime.UtcNow;

        public string? UserId { get; set; }
        public Guid? UserGuid { get; set; }

        [MaxLength(100)]
        public string? Action { get; set; }

        [MaxLength(100)]
        public string? EntityType { get; set; }

        public Guid? EntityId { get; set; }

        [MaxLength(50)]
        public string? Level { get; set; } = "Info";

        // Navigation properties
        public Tenant? Tenant { get; set; }
    }

    public class ErrorLog
    {
        [Key]
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }

        [Required]
        [MaxLength(2000)]
        public string Message { get; set; } = string.Empty;

        [MaxLength(5000)]
        public string? StackTrace { get; set; }

        [Required]
        public DateTime Date { get; set; } = DateTime.UtcNow;

        public string? UserId { get; set; }
        public Guid? UserGuid { get; set; }

        [MaxLength(200)]
        public string? Source { get; set; }

        [MaxLength(50)]
        public string? Severity { get; set; } = "Error";

        // Navigation properties
        public Tenant? Tenant { get; set; }
    }
}
