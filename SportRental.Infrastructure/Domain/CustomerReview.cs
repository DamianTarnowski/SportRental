using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SportRental.Infrastructure.Domain
{
    /// <summary>
    /// Ocena KLIENTA wystawiona PRZEZ wypożyczalnię po zakończonym wypożyczeniu.
    /// Świadomie brak pola Comment — RODO-friendly. Inne wypożyczalnie widzą tylko agregat
    /// (Customer.TrustLevel), nie indywidualne wpisy ani które tenant je wystawił.
    /// Jeden review per RentalId (unique constraint) — wypożyczalnia może ocenić klienta tylko
    /// raz per zakończony wynajem.
    /// </summary>
    public class CustomerReview
    {
        [Key]
        public Guid Id { get; set; }

        public Guid TenantId { get; set; }

        public Guid CustomerId { get; set; }

        public Guid RentalId { get; set; }

        /// <summary>Terminowość zwrotu (0-10). Czy oddał na czas, bez opóźnień?</summary>
        [Range(0, 10)]
        public int TimelinessScore { get; set; }

        /// <summary>Stan sprzętu po zwrocie (0-10). Czysty, sprawny, bez uszkodzeń?</summary>
        [Range(0, 10)]
        public int ConditionScore { get; set; }

        /// <summary>Komunikacja (0-10). Łatwy kontakt, odpowiada na wiadomości?</summary>
        [Range(0, 10)]
        public int CommunicationScore { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public Customer? Customer { get; set; }
        public Rental? Rental { get; set; }
        public Tenant? Tenant { get; set; }

        [NotMapped]
        public double AverageScore => (TimelinessScore + ConditionScore + CommunicationScore) / 3.0;

        [NotMapped]
        public bool IsIncident =>
            TimelinessScore < 5 || ConditionScore < 5 || CommunicationScore < 5;
    }
}
