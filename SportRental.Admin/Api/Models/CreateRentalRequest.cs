using System.ComponentModel.DataAnnotations;

namespace SportRental.Admin.Api.Models
{
    public class CreateRentalRequest
    {
        [Required]
        public Guid CustomerId { get; set; }

        [Required]
        public DateTime StartDateUtc { get; set; }

        [Required]
        public DateTime EndDateUtc { get; set; }

        [Required]
        [MinLength(1)]
        public List<CreateRentalItem> Items { get; set; } = new();

        // Optional: for idempotency support (future use)
        public string? IdempotencyKey { get; set; }

        // Typ wynajmu (godzinowy/dzienny)
        public RentalType RentalType { get; set; } = RentalType.Daily;
        public int? HoursRented { get; set; }
    }

    public enum RentalType
    {
        Daily = 0,
        Hourly = 1
    }

    public class CreateRentalItem
    {
        [Required]
        public Guid ProductId { get; set; }

        [Range(1, int.MaxValue)]
        public int Quantity { get; set; }
    }
}




