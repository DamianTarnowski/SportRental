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

        // Rezygnacja z e-maili z prośbą o opinię. Link rezygnacji w mailu.
        public bool ReviewEmailsOptOut { get; set; } = false;

        // === Trust scoring (RODO-friendly) — agregat z CustomerReview, recalc po insercie review.
        // Cross-tenant: liczone ze WSZYSTKICH wypożyczalni, ale klient widzi tylko poziom (TrustLevel),
        // nie ma dostępu do który-tenant-co-wystawił.
        public CustomerTrustLevel TrustLevel { get; set; } = CustomerTrustLevel.Unverified;
        public DateTime? TrustLevelCalculatedAtUtc { get; set; }
        public int TrustCompletedRentalsCount { get; set; } = 0;  // ile rentali Completed (z którymi chce się liczyć — różne tenanty)
        public double TrustAverageScore { get; set; } = 0;        // średnia z 3 wymiarów × wszystkich ocen
        public int TrustIncidentCount { get; set; } = 0;          // ile recenzji ze score < 5 w którymś z 3 wymiarów

        // Manual override przez admina — jeśli ustawione, używa tego zamiast wyliczonego.
        // Powód niewidoczny dla innych tenantów (RODO), tylko dla wystawiającego.
        public CustomerTrustLevel? TrustLevelManualOverride { get; set; }
        [MaxLength(500)]
        public string? TrustLevelManualReason { get; set; }
    }

    /// <summary>
    /// Status zaufania klienta wyliczany z historii ocen wystawionych przez wypożyczalnie.
    /// RODO-friendly: bez tekstów ani komentarzy, tylko liczby agregowane do poziomu.
    /// </summary>
    public enum CustomerTrustLevel
    {
        /// <summary>Nowy klient, mniej niż 10 zwrotów. Domyślny przy rejestracji.</summary>
        Unverified = 0,
        /// <summary>10+ zwrotów, średnia ≥ 8.0, brak incydentów. "Bez szkód".</summary>
        Good = 1,
        /// <summary>Średnia 5.0-8.0 albo 1-2 incydenty. "Wymaga uwagi".</summary>
        Watch = 2,
        /// <summary>Średnia &lt; 5.0 lub 3+ incydentów lub admin block. "Konto ograniczone".</summary>
        Restricted = 3
    }
}




