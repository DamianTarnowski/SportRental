namespace SportRental.Admin.Api.Models
{
    public class MyRentalDto
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public DateTime StartDateUtc { get; set; }
        public DateTime EndDateUtc { get; set; }
        public int Quantity { get; set; }
        public decimal TotalAmount { get; set; }
        public string Status { get; set; } = string.Empty;
        public bool CanCancel { get; set; }
    }
}
