using System;

namespace SportRental.Shared.Models
{
    public class CreateHoldRequest
    {
        public Guid ProductId { get; set; }
        public int Quantity { get; set; } = 1;
        public DateTime StartDateUtc { get; set; }
        public DateTime EndDateUtc { get; set; }
        public int? TtlMinutes { get; set; }
        public Guid? CustomerId { get; set; }
        public string? SessionId { get; set; }
    }
}
