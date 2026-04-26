using System.ComponentModel.DataAnnotations;

namespace SportRental.Shared.Models
{
    public class CreateCustomerReviewRequest
    {
        [Required]
        public Guid RentalId { get; set; }

        [Range(0, 10)]
        public int TimelinessScore { get; set; }

        [Range(0, 10)]
        public int ConditionScore { get; set; }

        [Range(0, 10)]
        public int CommunicationScore { get; set; }
    }

    /// <summary>
    /// Agregat zaufania klienta widoczny dla każdej wypożyczalni (cross-tenant). Bez detali
    /// per-tenant ani komentarzy — RODO-friendly.
    /// </summary>
    public class CustomerTrustSummaryDto
    {
        public Guid CustomerId { get; set; }
        /// <summary>0=Unverified, 1=Good, 2=Watch, 3=Restricted</summary>
        public int TrustLevel { get; set; }
        public string TrustLabel { get; set; } = string.Empty;       // "Bez szkód", "Wymaga uwagi", itp.
        public string TrustEmoji { get; set; } = string.Empty;       // "🟢", "🟡", "🔴", "✅"
        public int CompletedRentals { get; set; }
        public double AverageScore { get; set; }
        public int IncidentCount { get; set; }
        public DateTime? CalculatedAtUtc { get; set; }
        public bool IsManualOverride { get; set; }
    }

    public class UpdateCustomerTrustOverrideRequest
    {
        /// <summary>null = wyłącz override (auto-recompute); 0-3 = wymuś poziom</summary>
        public int? TrustLevel { get; set; }
        [StringLength(500)]
        public string? Reason { get; set; }
    }
}
