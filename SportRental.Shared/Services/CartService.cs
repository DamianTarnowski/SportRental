using System.Text.Json;
using Microsoft.JSInterop;
using SportRental.Shared.Models;

namespace SportRental.Shared.Services;

public class CartService : ICartService
{
    private readonly IJSRuntime _jsRuntime;
    private readonly IApiService _apiService;
    private Cart _cart = new();
    private const string CART_KEY = "sport-rental-cart";
    private static readonly TimeSpan DefaultRefreshBeforeExpiry = TimeSpan.FromMinutes(2);
    private IReadOnlyCollection<Guid> _lastUnavailableProducts = Array.Empty<Guid>();

    public event EventHandler? CartChanged;

    public CartService(IJSRuntime jsRuntime, IApiService apiService)
    {
        _jsRuntime = jsRuntime;
        _apiService = apiService;
        _ = LoadCartFromStorageAsync();
    }

    public Cart GetCart() => _cart;
    public IReadOnlyCollection<Guid> LastUnavailableProductIds => _lastUnavailableProducts;

    public async Task AddToCartAsync(ProductDto product, int quantity = 1, DateTime? startDate = null, DateTime? endDate = null)
    {
        if (startDate.HasValue && endDate.HasValue && endDate <= startDate)
        {
            throw new ArgumentException("End date must be later than start date", nameof(endDate));
        }

        _cart.AddItem(product, quantity, startDate, endDate);
        await SaveCartToStorageAsync();
        CartChanged?.Invoke(this, EventArgs.Empty);

        // Attempt to secure holds immediately when dates are provided
        if (startDate.HasValue && endDate.HasValue)
        {
            _ = EnsureHoldsAsync();
        }
    }

    public async Task RemoveFromCartAsync(Guid productId)
    {
        // Release hold if exists
        var item = _cart.Items.FirstOrDefault(i => i.ProductId == productId);
        if (item?.HoldId is Guid hid)
        {
            _ = _apiService.DeleteHoldAsync(hid); // fire-and-forget
        }
        _cart.RemoveItem(productId);
        await SaveCartToStorageAsync();
        CartChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task UpdateQuantityAsync(Guid productId, int quantity)
    {
        _cart.UpdateQuantity(productId, quantity);
        await SaveCartToStorageAsync();
        CartChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task UpdateDatesAsync(Guid productId, DateTime startDate, DateTime endDate)
    {
        var item = _cart.Items.FirstOrDefault(i => i.ProductId == productId);
        if (item != null)
        {
            item.StartDate = startDate;
            item.EndDate = endDate;
            // Dates changed: previous hold becomes invalid; release it
            if (item.HoldId is Guid hid)
            {
                _ = _apiService.DeleteHoldAsync(hid);
                item.HoldId = null;
                item.HoldExpiresAtUtc = null;
            }
            await SaveCartToStorageAsync();
            CartChanged?.Invoke(this, EventArgs.Empty);
            await EnsureHoldsAsync();
        }
    }

    public async Task ClearCartAsync()
    {
        // Release all holds
        foreach (var it in _cart.Items)
        {
            if (it.HoldId is Guid hid)
            {
                _ = _apiService.DeleteHoldAsync(hid);
            }
        }
        _cart.Clear();
        await SaveCartToStorageAsync();
        CartChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<bool> ValidateAvailabilityAsync()
    {
        _lastUnavailableProducts = Array.Empty<Guid>();

        if (_cart.Items.Count == 0)
        {
            return true;
        }

        try
        {
            var requestedIds = _cart.Items.Select(i => i.ProductId).Distinct().ToList();
            if (requestedIds.Count == 0)
            {
                return true;
            }

            var catalog = await _apiService.GetProductsAsync(1, 200);
            var lookup = catalog.ToDictionary(p => p.Id, p => p);

            var unavailable = new HashSet<Guid>();
            foreach (var cartItem in _cart.Items)
            {
                if (!lookup.TryGetValue(cartItem.ProductId, out var product) ||
                    !product.IsAvailable ||
                    product.AvailableQuantity < cartItem.Quantity)
                {
                    unavailable.Add(cartItem.ProductId);
                }
            }

            _lastUnavailableProducts = unavailable.ToList();
            return unavailable.Count == 0;
        }
        catch
        {
            // Unable to verify availability – block checkout and require retry
            _lastUnavailableProducts = _cart.Items.Select(i => i.ProductId).ToList();
            return false;
        }
    }

    public async Task<bool> EnsureHoldsAsync()
    {
        var success = true;
        foreach (var it in _cart.Items)
        {
            if (it.HoldId == null)
            {
                var resp = await _apiService.CreateHoldAsync(new CreateHoldRequest
                {
                    ProductId = it.ProductId,
                    Quantity = it.Quantity,
                    StartDateUtc = it.StartDate.ToUniversalTime(),
                    EndDateUtc = it.EndDate.ToUniversalTime(),
                    TtlMinutes = 10
                });
                if (resp == null)
                {
                    success = false;
                }
                else
                {
                    it.HoldId = resp.Id;
                    it.HoldExpiresAtUtc = resp.ExpiresAtUtc;
                }
            }
        }
        await SaveCartToStorageAsync();
        if (success) CartChanged?.Invoke(this, EventArgs.Empty);
        return success;
    }

    public async Task RefreshHoldsIfNeededAsync(TimeSpan? beforeExpiry = null)
    {
        var threshold = beforeExpiry ?? DefaultRefreshBeforeExpiry;
        var nowUtc = DateTime.UtcNow;
        foreach (var it in _cart.Items)
        {
            if (it.HoldId == null || it.HoldExpiresAtUtc == null) continue;
            if (it.HoldExpiresAtUtc.Value - nowUtc <= threshold)
            {
                // Recreate hold (no extend endpoint yet)
                var newHold = await _apiService.CreateHoldAsync(new CreateHoldRequest
                {
                    ProductId = it.ProductId,
                    Quantity = it.Quantity,
                    StartDateUtc = it.StartDate.ToUniversalTime(),
                    EndDateUtc = it.EndDate.ToUniversalTime(),
                    TtlMinutes = 10
                });
                if (newHold != null)
                {
                    // release old hold
                    var old = it.HoldId.Value;
                    _ = _apiService.DeleteHoldAsync(old);
                    it.HoldId = newHold.Id;
                    it.HoldExpiresAtUtc = newHold.ExpiresAtUtc;
                }
            }
        }
        await SaveCartToStorageAsync();
        CartChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task ReleaseHoldAsync(Guid productId)
    {
        var it = _cart.Items.FirstOrDefault(i => i.ProductId == productId);
        if (it?.HoldId is Guid hid)
        {
            await _apiService.DeleteHoldAsync(hid);
            it.HoldId = null;
            it.HoldExpiresAtUtc = null;
            await SaveCartToStorageAsync();
            CartChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task ReleaseAllHoldsAsync()
    {
        foreach (var it in _cart.Items)
        {
            if (it.HoldId is Guid hid)
            {
                _ = _apiService.DeleteHoldAsync(hid);
                it.HoldId = null;
                it.HoldExpiresAtUtc = null;
            }
        }
        await SaveCartToStorageAsync();
        CartChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task SaveCartToStorageAsync()
    {
        try
        {
            var json = JsonSerializer.Serialize(_cart);
            await _jsRuntime.InvokeVoidAsync("localStorage.setItem", CART_KEY, json);
        }
        catch (Exception)
        {
            // Handle JS interop errors silently
        }
    }

    private async Task LoadCartFromStorageAsync()
    {
        try
        {
            var json = await _jsRuntime.InvokeAsync<string>("localStorage.getItem", CART_KEY);
            if (!string.IsNullOrEmpty(json))
            {
                var cart = JsonSerializer.Deserialize<Cart>(json);
                if (cart != null)
                {
                    _cart = cart;
                    CartChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }
        catch (Exception)
        {
            // Handle JS interop errors silently
            _cart = new Cart();
        }
    }
}
