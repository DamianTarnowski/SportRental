namespace SportRental.Shared.Models
{
    public class MyRentalDto
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public DateTime StartDateUtc { get; set; }
        public DateTime EndDateUtc { get; set; }
        public int Quantity { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal DepositAmount { get; set; }
        public string PaymentStatus { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public bool CanCancel { get; set; }
        public string? ContractUrl { get; set; }
        public List<MyRentalItemDto> Items { get; set; } = new();
    }

    public class MyRentalItemDto
    {
        public Guid ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal DailyPrice { get; set; }
        public decimal TotalPrice { get; set; }
    }
}
