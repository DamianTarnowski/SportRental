using System.ComponentModel.DataAnnotations;

namespace SportRental.Infrastructure.Domain
{
    public class Tenant
    {
        public Guid Id { get; set; }

        [Required]
        [MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        [MaxLength(512)]
        public string? LogoUrl { get; set; }

        [MaxLength(16)]
        public string? PrimaryColorHex { get; set; }

        [MaxLength(16)]
        public string? SecondaryColorHex { get; set; }

        /// Tryb demo — tenant utworzony przez kliknięcie "Wypróbuj demo".
        /// Cleanup background service usuwa demo tenants po DemoExpiresAtUtc.
        public bool IsDemo { get; set; }

        /// Czas wygaśnięcia trybu demo (null dla zwykłych tenantów).
        public DateTime? DemoExpiresAtUtc { get; set; }
    }
}


