namespace SportRental.Infrastructure.Domain;

/// <summary>
/// Jednorazowy, krótko żyjący link pozwalający gościowi odzyskać dostęp do
/// własnego zamówienia po wygaśnięciu cookie. W bazie zapisujemy wyłącznie hash
/// tokenu; surowa wartość trafia tylko do wiadomości e-mail.
/// </summary>
public sealed class GuestOrderAccessToken
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public Guid MarketplaceOrderId { get; set; }
    public MarketplaceOrder? MarketplaceOrder { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? UsedAtUtc { get; set; }
    public string? RequestedFromIp { get; set; }
}
