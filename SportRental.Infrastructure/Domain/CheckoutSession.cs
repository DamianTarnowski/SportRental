namespace SportRental.Infrastructure.Domain;

public class CheckoutSession
{
    public Guid Id { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public string? StripeSessionId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public bool IsProcessed { get; set; }
    public string? FailureReason { get; set; }
    public DateTime? RefundedAtUtc { get; set; }
    public string? AcceptedTermsVersion { get; set; }
    public string? AcknowledgedPrivacyVersion { get; set; }
    public DateTime? LegalAcceptedAtUtc { get; set; }
}
