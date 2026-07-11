namespace SportRental.Shared.Models;

public record CreateCheckoutSessionRequest(
    DateTime StartDateUtc,
    DateTime EndDateUtc,
    List<CheckoutItem> Items,
    string CustomerEmail,
    Guid? CustomerId = null,
    RentalTypeDto RentalType = RentalTypeDto.Daily,
    int? HoursRented = null,
    string? Notes = null,
    string? HoldSessionId = null,
    string? AcceptedTermsVersion = null,
    string? AcknowledgedPrivacyVersion = null,
    IReadOnlyList<CheckoutRentalGroupRequest>? RentalGroups = null);

public record CheckoutItem(Guid ProductId, int Quantity, Guid? HoldId = null);

/// <summary>
/// Jedna rezerwacja tworzona dla konkretnej wypożyczalni w ramach wspólnego
/// checkoutu marketplace. Wszystkie pozycje grupy mają ten sam termin i typ
/// wynajmu, ale inne wypożyczalnie mogą mieć inne terminy.
/// </summary>
public record CheckoutRentalGroupRequest(
    Guid TenantId,
    DateTime StartDateUtc,
    DateTime EndDateUtc,
    List<CheckoutItem> Items,
    RentalTypeDto RentalType = RentalTypeDto.Daily,
    int? HoursRented = null,
    string? AcceptedRegulationsHash = null);

public record CheckoutSessionResponse(
    string SessionId,
    string Url,
    DateTime ExpiresAt,
    DateTime? HoldExpiresAtUtc = null);

public record FinalizeSessionResponse(
    bool Success,
    string Message,
    Guid? RentalId,
    IReadOnlyList<Guid>? RentalIds = null,
    bool Refunded = false,
    bool Retryable = false,
    Guid? MarketplaceOrderId = null,
    string? MarketplaceOrderNumber = null);
