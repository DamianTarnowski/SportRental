namespace SportRental.Shared.Models
{
    public class MyRentalDto
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid? MarketplaceOrderId { get; set; }
        public string? MarketplaceOrderNumber { get; set; }
        public int? OrderSequence { get; set; }
        public int OrderRentalCount { get; set; } = 1;
        public string TenantName { get; set; } = string.Empty;
        public string? PickupAddress { get; set; }
        public string? PickupCity { get; set; }
        public string? TenantPhoneNumber { get; set; }
        public string? TenantEmail { get; set; }
        public string? OpeningHours { get; set; }
        public string Title { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public DateTime StartDateUtc { get; set; }
        public DateTime EndDateUtc { get; set; }
        public int Quantity { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal DepositAmount { get; set; }
        public decimal PaidAmount { get; set; }
        public DateTime? DepositPaidAtUtc { get; set; }
        public string PaymentStatus { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public bool CanCancel { get; set; }
        public string? ContractUrl { get; set; }
        public string? PublicContractUrl { get; set; }
        public bool HasReview { get; set; }
        public RentalTypeDto RentalType { get; set; } = RentalTypeDto.Daily;
        public int? HoursRented { get; set; }
        public string? PaymentMethod { get; set; }
        public DateTime? PaidAtUtc { get; set; }
        public decimal? DamageCharge { get; set; }
        public bool IsSmsConfirmed { get; set; }
        public List<MyRentalItemDto> Items { get; set; } = new();
        
        // Nowe pola do śledzenia wydania/zwrotu
        public DateTime? IssuedAtUtc { get; set; }
        public DateTime? ReturnedAtUtc { get; set; }
        public string? IssueNotes { get; set; }
        public string? ReturnNotes { get; set; }
        public decimal? ReturnDepositRefund { get; set; }
        public bool IsOverdue => IssuedAtUtc.HasValue && !ReturnedAtUtc.HasValue && EndDateUtc < DateTime.UtcNow;
    }

    public class MyRentalItemDto
    {
        public Guid RentalItemId { get; set; }
        public Guid ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal DailyPrice { get; set; }
        public decimal? HourlyPrice { get; set; }
        public decimal TotalPrice { get; set; }
    }
}
