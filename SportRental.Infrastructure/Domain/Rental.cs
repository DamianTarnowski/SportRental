namespace SportRental.Infrastructure.Domain
{
    public enum RentalStatus
    {
        Draft = 0,
        Pending = 1,
        Confirmed = 2,
        Active = 3,
        Completed = 4,
        Cancelled = 5
    }

    /// <summary>
    /// Źródło wypożyczenia - online (WASM klient) lub fizyczne (w wypożyczalni)
    /// </summary>
    public enum RentalSource
    {
        Online = 0,     // Wypożyczenie przez klienta WASM
        InStore = 1     // Wypożyczenie fizyczne w wypożyczalni
    }

    public class Rental
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }

        public Guid CustomerId { get; set; }
        public Customer? Customer { get; set; }

        public DateTime StartDateUtc { get; set; }
        public DateTime EndDateUtc { get; set; }

        public RentalStatus Status { get; set; } = RentalStatus.Draft;

        public decimal TotalAmount { get; set; }
        public decimal DepositAmount { get; set; }
        public string? PaymentIntentId { get; set; }
        public string PaymentStatus { get; set; } = string.Empty;
        public string? ContractUrl { get; set; }
        public string? Notes { get; set; }
        public string? IdempotencyKey { get; set; }

        // SMS and Email tracking
        public bool IsSmsConfirmed { get; set; } = false;
        public bool IsEmailSent { get; set; } = false;

        // Źródło wypożyczenia
        public RentalSource Source { get; set; } = RentalSource.Online;

        // Wydanie sprzętu
        public DateTime? IssuedAtUtc { get; set; }
        public Guid? IssuedByEmployeeId { get; set; }
        public Employee? IssuedByEmployee { get; set; }
        public string? IssueNotes { get; set; }

        // Zwrot sprzętu
        public DateTime? ReturnedAtUtc { get; set; }
        public Guid? ReturnedByEmployeeId { get; set; }
        public Employee? ReturnedByEmployee { get; set; }
        public string? ReturnNotes { get; set; }
        public decimal? ReturnDepositRefund { get; set; }  // Ile z depozytu zwrócono
        public decimal? DamageCharge { get; set; }         // Opłata za uszkodzenia

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public List<RentalItem> Items { get; set; } = new();
    }
}
