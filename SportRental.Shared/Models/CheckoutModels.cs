namespace SportRental.Shared.Models;

public record CreateCheckoutSessionRequest(
    DateTime StartDateUtc,
    DateTime EndDateUtc,
    List<CheckoutItem> Items,
    string CustomerEmail,
    Guid? CustomerId = null,
    RentalTypeDto RentalType = RentalTypeDto.Daily,
    int? HoursRented = null);

public record CheckoutItem(Guid ProductId, int Quantity);

public record CheckoutSessionResponse(
    string SessionId,
    string Url,
    DateTime ExpiresAt);

public record FinalizeSessionResponse(
    bool Success,
    string Message,
    Guid? RentalId);
