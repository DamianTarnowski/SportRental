namespace SportRental.Infrastructure.Domain;

/// <summary>
/// Nadrzędne zamówienie klienta w marketplace. Jedna płatność Stripe może
/// utworzyć kilka niezależnych Rental — po jednym dla każdej wypożyczalni.
/// Encja nie ma TenantId, ponieważ jest agregatem cross-tenant dostępnym tylko
/// klientowi i procesowi finalizacji; panele tenantów nadal pracują na Rental.
/// </summary>
public sealed class MarketplaceOrder
{
    public Guid Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public Guid CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public string? CustomerEmailSnapshot { get; set; }
    public Guid CheckoutSessionId { get; set; }
    public CheckoutSession? CheckoutSession { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? StripeSessionId { get; set; }
    public string? PaymentIntentId { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal DepositAmount { get; set; }
    public decimal RefundedDepositAmount { get; set; }
    public string Currency { get; set; } = "PLN";
    public string Status { get; set; } = "Confirmed";
    public string PaymentStatus { get; set; } = "DepositPaid";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? PaidAtUtc { get; set; }
    public string AcceptedTermsVersion { get; set; } = string.Empty;
    public string AcknowledgedPrivacyVersion { get; set; } = string.Empty;
    public DateTime? LegalAcceptedAtUtc { get; set; }
    public List<Rental> Rentals { get; set; } = new();
}
