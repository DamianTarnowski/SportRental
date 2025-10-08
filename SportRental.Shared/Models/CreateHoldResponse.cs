using System;

namespace SportRental.Shared.Models
{
    public class CreateHoldResponse
    {
        public Guid Id { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
    }
}
