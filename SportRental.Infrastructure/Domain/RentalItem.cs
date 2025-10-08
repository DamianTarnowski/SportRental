namespace SportRental.Infrastructure.Domain
{
    public class RentalItem
    {
        public Guid Id { get; set; }
        public Guid RentalId { get; set; }
        public Rental? Rental { get; set; }

        public Guid ProductId { get; set; }
        public Product? Product { get; set; }

        public int Quantity { get; set; } = 1;
        public decimal PricePerDay { get; set; }
        public decimal Subtotal { get; set; }
    }
}




