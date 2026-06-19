using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SportRental.Infrastructure.Domain
{
    /// <summary>
    /// Opinia wystawiona przez klienta po zakończonym wynajmie.
    /// Tylko klient który faktycznie wypożyczył sprzęt (Rental.CustomerId = CustomerId)
    /// i wynajem jest w statusie Completed może wystawić opinię.
    /// Jeden review per Rental (unique index).
    /// </summary>
    public class RentalReview
    {
        [Key]
        public Guid Id { get; set; }

        public Guid TenantId { get; set; }

        public Guid RentalId { get; set; }

        public Guid CustomerId { get; set; }

        [Range(0, 10)]
        public int QualityScore { get; set; }

        [Range(0, 10)]
        public int PriceScore { get; set; }

        [Range(0, 10)]
        public int ServiceScore { get; set; }

        [MaxLength(2000)]
        public string? Comment { get; set; }

        // Odpowiedź właściciela (Maciej) — wystawiana po wystawieniu recenzji przez klienta.
        // Widoczna publicznie obok komentarza klienta.
        [MaxLength(2000)]
        public string? OwnerReply { get; set; }
        public DateTime? OwnerReplyAtUtc { get; set; }

        public bool IsHidden { get; set; } = false;

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public Rental? Rental { get; set; }
        public Customer? Customer { get; set; }
        public Tenant? Tenant { get; set; }
        public ICollection<RentalItemReview> ItemReviews { get; set; } = new List<RentalItemReview>();

        [NotMapped]
        public double AverageScore => (QualityScore + PriceScore + ServiceScore) / 3.0;
    }
}
