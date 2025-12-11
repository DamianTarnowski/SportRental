using SportRental.Shared.Models;

namespace SportRental.Shared.Services;

public interface ICartService
{
    event EventHandler? CartChanged;
    Cart GetCart();
    IReadOnlyCollection<Guid> LastUnavailableProductIds { get; }
    Task AddToCartAsync(ProductDto product, int quantity = 1, DateTime? startDate = null, DateTime? endDate = null);
    Task RemoveFromCartAsync(Guid productId);
    Task UpdateQuantityAsync(Guid productId, int quantity);
    Task UpdateDatesAsync(Guid productId, DateTime startDate, DateTime endDate);
    Task UpdateRentalTypeAsync(Guid productId, RentalTypeDto rentalType, int? hoursRented);
    Task ClearCartAsync();
    Task<bool> ValidateAvailabilityAsync();

    // Holds lifecycle
    Task<bool> EnsureHoldsAsync();
    Task RefreshHoldsIfNeededAsync(TimeSpan? beforeExpiry = null);
    Task ReleaseHoldAsync(Guid productId);
    Task ReleaseAllHoldsAsync();
}
