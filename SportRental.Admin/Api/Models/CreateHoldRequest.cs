using System.ComponentModel.DataAnnotations;

namespace SportRental.Admin.Api.Models
{
    public class CreateHoldRequest
    {
        [Required]
        public Guid ProductId { get; set; }

        [Range(1, int.MaxValue)]
        public int Quantity { get; set; } = 1;

        [Required]
        public DateTime StartDateUtc { get; set; }

        [Required]
        public DateTime EndDateUtc { get; set; }

        // Optional: default 10 minutes, clamped to 5..30
        public int? TtlMinutes { get; set; }

        // Optional identifiers (for linking to user/session/cart)
        public Guid? CustomerId { get; set; }
        public string? SessionId { get; set; }
    }
}
