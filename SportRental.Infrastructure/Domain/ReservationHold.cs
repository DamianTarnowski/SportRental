namespace SportRental.Infrastructure.Domain
{
    public class ReservationHold
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }

        public Guid ProductId { get; set; }
        public int Quantity { get; set; }

        public DateTime StartDateUtc { get; set; }
        public DateTime EndDateUtc { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime ExpiresAtUtc { get; set; }

        public Guid? CustomerId { get; set; }
        public string? SessionId { get; set; }
    }
}
