using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SportRental.Infrastructure.Domain
{
    /// <summary>
    /// Opcjonalna ocena konkretnego sprzętu z wynajmu — per RentalItem. Tworzona razem z
    /// RentalReview gdy klient w formularzu ankiety rozbije ocenę na poszczególne sprzęty.
    /// Domyślnie nie jest wymagane (klient może wystawić tylko główną opinię o wynajmie).
    /// </summary>
    public class RentalItemReview
    {
        [Key]
        public Guid Id { get; set; }

        public Guid RentalReviewId { get; set; }

        public Guid RentalItemId { get; set; }

        public Guid ProductId { get; set; }

        [Range(0, 10)]
        public int Rating { get; set; }

        [MaxLength(1000)]
        public string? Comment { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public RentalReview? RentalReview { get; set; }
        public RentalItem? RentalItem { get; set; }
        public Product? Product { get; set; }
    }
}
