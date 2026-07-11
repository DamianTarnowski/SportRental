using System.Text.Json;
using Microsoft.JSInterop;
using SportRental.Shared.Models;
using SportRental.Shared.Time;

namespace SportRental.Shared.Services;

public class CartService : ICartService
{
    private readonly IJSRuntime _jsRuntime;
    private readonly IApiService _apiService;
    private Cart _cart = new();
    private const string CART_KEY = "sport-rental-cart";
    private const string HOLD_SESSION_KEY = "sport-rental-hold-session";
    private static readonly TimeSpan DefaultRefreshBeforeExpiry = TimeSpan.FromMinutes(2);
    private IReadOnlyCollection<Guid> _lastUnavailableProducts = Array.Empty<Guid>();
    private string? _holdSessionId;
    private readonly Task _loadTask;

    public event EventHandler? CartChanged;

    public CartService(IJSRuntime jsRuntime, IApiService apiService)
    {
        _jsRuntime = jsRuntime;
        _apiService = apiService;
        _loadTask = LoadCartFromStorageAsync();
    }

    public async Task<string> GetHoldSessionIdAsync()
    {
        if (!string.IsNullOrWhiteSpace(_holdSessionId)) return _holdSessionId!;
        try
        {
            var existing = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", HOLD_SESSION_KEY);
            if (!string.IsNullOrWhiteSpace(existing))
            {
                _holdSessionId = existing;
                return existing;
            }
            var fresh = Guid.NewGuid().ToString("N");
            await _jsRuntime.InvokeVoidAsync("localStorage.setItem", HOLD_SESSION_KEY, fresh);
            _holdSessionId = fresh;
            return fresh;
        }
        catch
        {
            _holdSessionId = Guid.NewGuid().ToString("N");
            return _holdSessionId;
        }
    }

    public Cart GetCart() => _cart;
    public IReadOnlyCollection<Guid> LastUnavailableProductIds => _lastUnavailableProducts;
    public string? LastHoldError { get; private set; }

    public async Task AddToCartAsync(ProductDto product, int quantity = 1, DateTime? startDate = null, DateTime? endDate = null)
    {
        await _loadTask;
        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be greater than zero.");
        if (startDate.HasValue && endDate.HasValue && endDate <= startDate)
        {
            throw new ArgumentException("End date must be later than start date", nameof(endDate));
        }

        var existing = _cart.Items.FirstOrDefault(i => i.ProductId == product.Id);
        var tenantTemplate = product.TenantId == Guid.Empty
            ? null
            : _cart.Items.FirstOrDefault(i => i.TenantId == product.TenantId);
        if (existing?.HoldId is Guid existingHoldId)
        {
            await DeleteHoldWithSessionAsync(existingHoldId);
            existing.HoldId = null;
            existing.HoldExpiresAtUtc = null;
        }

        // A checkout creates one rental per tenant, so all products from that tenant must
        // share one set of rental terms. Treat the first tenant item as the canonical
        // template even when an already-added product is being added again. This also
        // repairs a previously inconsistent item instead of preserving its stale terms.
        var effectiveStartDate = tenantTemplate?.StartDate ?? existing?.StartDate ?? startDate;
        var effectiveEndDate = tenantTemplate?.EndDate ?? existing?.EndDate ?? endDate;
        _cart.AddItem(product, quantity, effectiveStartDate, effectiveEndDate);

        var addedItem = _cart.Items.First(i => i.ProductId == product.Id);
        if (tenantTemplate is not null)
        {
            addedItem.StartDate = tenantTemplate.StartDate;
            addedItem.EndDate = tenantTemplate.EndDate;
            addedItem.RentalType = tenantTemplate.RentalType;
            addedItem.HoursRented = tenantTemplate.HoursRented;
        }
        await SaveCartToStorageAsync();
        CartChanged?.Invoke(this, EventArgs.Empty);

        // Attempt to secure holds immediately when dates are provided
        if (startDate.HasValue && endDate.HasValue)
        {
            await EnsureHoldsAsync();
        }
    }

    public async Task RemoveFromCartAsync(Guid productId)
    {
        await _loadTask;
        // Release hold if exists
        var item = _cart.Items.FirstOrDefault(i => i.ProductId == productId);
        if (item?.HoldId is Guid hid)
        {
            await DeleteHoldWithSessionAsync(hid);
        }
        _cart.RemoveItem(productId);
        await SaveCartToStorageAsync();
        CartChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task UpdateQuantityAsync(Guid productId, int quantity)
    {
        await _loadTask;
        var item = _cart.Items.FirstOrDefault(i => i.ProductId == productId);
        if (item?.HoldId is Guid holdId)
        {
            await DeleteHoldWithSessionAsync(holdId);
            item.HoldId = null;
            item.HoldExpiresAtUtc = null;
        }
        _cart.UpdateQuantity(productId, quantity);
        await SaveCartToStorageAsync();
        CartChanged?.Invoke(this, EventArgs.Empty);
        if (quantity > 0)
            await EnsureHoldsAsync();
    }

    public async Task UpdateDatesAsync(Guid productId, DateTime startDate, DateTime endDate)
    {
        await _loadTask;
        var item = _cart.Items.FirstOrDefault(i => i.ProductId == productId);
        if (item != null)
        {
            item.StartDate = startDate;
            item.EndDate = endDate;
            // Dates changed: previous hold becomes invalid; release it
            if (item.HoldId is Guid hid)
            {
                await DeleteHoldWithSessionAsync(hid);
                item.HoldId = null;
                item.HoldExpiresAtUtc = null;
            }
            await SaveCartToStorageAsync();
            CartChanged?.Invoke(this, EventArgs.Empty);
            await EnsureHoldsAsync();
        }
    }

    public async Task UpdateRentalTypeAsync(Guid productId, Models.RentalTypeDto rentalType, int? hoursRented)
    {
        await _loadTask;
        var item = _cart.Items.FirstOrDefault(i => i.ProductId == productId);
        if (item != null)
        {
            item.RentalType = rentalType;
            item.HoursRented = rentalType == Models.RentalTypeDto.Hourly ? hoursRented : null;
            await SaveCartToStorageAsync();
            CartChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task UpdateTenantRentalTermsAsync(
        Guid tenantId,
        DateTime startDate,
        DateTime endDate,
        Models.RentalTypeDto rentalType,
        int? hoursRented)
    {
        await _loadTask;
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId is required.", nameof(tenantId));
        if (endDate <= startDate)
            throw new ArgumentException("End date must be later than start date.", nameof(endDate));
        if (rentalType == Models.RentalTypeDto.Hourly && hoursRented is not (>= 1 and <= 24))
            throw new ArgumentOutOfRangeException(nameof(hoursRented), "Hourly rental must last from 1 to 24 hours.");

        var tenantItems = _cart.Items.Where(item => item.TenantId == tenantId).ToList();
        if (tenantItems.Count == 0)
            return;

        foreach (var item in tenantItems)
        {
            if (item.HoldId is Guid holdId)
                await DeleteHoldWithSessionAsync(holdId);

            item.HoldId = null;
            item.HoldExpiresAtUtc = null;
            item.StartDate = startDate;
            item.EndDate = endDate;
            item.RentalType = rentalType;
            item.HoursRented = rentalType == Models.RentalTypeDto.Hourly ? hoursRented : null;
        }

        await SaveCartToStorageAsync();
        CartChanged?.Invoke(this, EventArgs.Empty);
        await EnsureHoldsAsync();
    }

    public async Task ClearCartAsync()
    {
        await _loadTask;
        // Release all holds
        foreach (var it in _cart.Items)
        {
            if (it.HoldId is Guid hid)
            {
                await DeleteHoldWithSessionAsync(hid);
            }
        }
        _cart.Clear();
        await SaveCartToStorageAsync();
        CartChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<bool> ValidateAvailabilityAsync()
    {
        await _loadTask;
        _lastUnavailableProducts = Array.Empty<Guid>();

        if (_cart.Items.Count == 0)
        {
            return true;
        }

        try
        {
            var requestedIds = _cart.Items.Select(i => i.ProductId).Distinct().ToList();
            var products = await Task.WhenAll(requestedIds.Select(_apiService.GetProductAsync));
            var lookup = products.Where(p => p is not null).ToDictionary(p => p!.Id, p => p!);

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
        await _loadTask;
        LastHoldError = null;
        var success = true;
        var nowUtc = DateTime.UtcNow;
        foreach (var it in _cart.Items)
        {
            if (!PolishRentalTime.TryToUtc(it.StartDate, out var startUtc) ||
                !PolishRentalTime.TryToUtc(it.EndDate, out var endUtc) ||
                !PolishRentalTime.IsStartSafelyInFuture(startUtc, nowUtc))
            {
                if (it.HoldId is Guid invalidHoldId)
                    await DeleteHoldWithSessionAsync(invalidHoldId);
                it.HoldId = null;
                it.HoldExpiresAtUtc = null;
                success = false;
                continue;
            }

            if (it.HoldId.HasValue && it.HoldExpiresAtUtc <= DateTime.UtcNow)
            {
                it.HoldId = null;
                it.HoldExpiresAtUtc = null;
            }
            if (it.HoldId == null)
            {
                var sessionId = await GetHoldSessionIdAsync();
                var resp = await _apiService.CreateHoldAsync(new CreateHoldRequest
                {
                    ProductId = it.ProductId,
                    Quantity = it.Quantity,
                    StartDateUtc = startUtc,
                    EndDateUtc = endUtc,
                    TtlMinutes = 10,
                    SessionId = sessionId
                });
                if (resp == null)
                {
                    LastHoldError ??= _apiService.LastHoldError;
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
        await _loadTask;
        var threshold = beforeExpiry ?? DefaultRefreshBeforeExpiry;
        var nowUtc = DateTime.UtcNow;
        var success = true;
        foreach (var it in _cart.Items)
        {
            if (it.HoldId == null || it.HoldExpiresAtUtc == null) continue;
            if (it.HoldExpiresAtUtc.Value - nowUtc <= threshold)
            {
                var sessionId = await GetHoldSessionIdAsync();
                var refreshed = await _apiService.RefreshHoldAsync(it.HoldId.Value, sessionId, 10);
                if (refreshed != null)
                {
                    it.HoldId = refreshed.Id;
                    it.HoldExpiresAtUtc = refreshed.ExpiresAtUtc;
                }
                else
                {
                    it.HoldId = null;
                    it.HoldExpiresAtUtc = null;
                    success = false;
                }
            }
        }
        await SaveCartToStorageAsync();
        CartChanged?.Invoke(this, EventArgs.Empty);
        if (!success)
            await EnsureHoldsAsync();
    }

    public async Task UpdateHoldExpirationsAsync(
        IReadOnlyCollection<Guid> holdIds,
        DateTime expiresAtUtc)
    {
        await _loadTask;
        if (holdIds.Count == 0)
            return;

        var ids = holdIds.ToHashSet();
        var changed = false;
        foreach (var item in _cart.Items)
        {
            if (item.HoldId is not Guid holdId || !ids.Contains(holdId))
                continue;

            item.HoldExpiresAtUtc = expiresAtUtc;
            changed = true;
        }

        if (!changed)
            return;

        await SaveCartToStorageAsync();
        CartChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task ReleaseHoldAsync(Guid productId)
    {
        await _loadTask;
        var it = _cart.Items.FirstOrDefault(i => i.ProductId == productId);
        if (it?.HoldId is Guid hid)
        {
            await DeleteHoldWithSessionAsync(hid);
            it.HoldId = null;
            it.HoldExpiresAtUtc = null;
            await SaveCartToStorageAsync();
            CartChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task DeleteHoldWithSessionAsync(Guid holdId)
    {
        var sessionId = await GetHoldSessionIdAsync();
        await _apiService.DeleteHoldAsync(holdId, sessionId);
    }

    public async Task ReleaseAllHoldsAsync()
    {
        await _loadTask;
        foreach (var it in _cart.Items)
        {
            if (it.HoldId is Guid hid)
            {
                await DeleteHoldWithSessionAsync(hid);
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
                    await HydrateTenantMetadataAsync();
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

    private async Task HydrateTenantMetadataAsync()
    {
        var itemsToHydrate = _cart.Items
            .Where(item => item.TenantId == Guid.Empty ||
                           string.IsNullOrWhiteSpace(item.TenantName) ||
                           string.IsNullOrWhiteSpace(item.PickupCity))
            .ToList();
        if (itemsToHydrate.Count == 0)
            return;

        var changed = false;
        foreach (var item in itemsToHydrate)
        {
            ProductDto? product;
            try
            {
                product = await _apiService.GetProductAsync(item.ProductId);
            }
            catch
            {
                continue;
            }

            if (product is null || product.TenantId == Guid.Empty)
                continue;

            item.TenantId = product.TenantId;
            item.TenantName = string.IsNullOrWhiteSpace(product.TenantName)
                ? "Wypożyczalnia"
                : product.TenantName;
            item.PickupAddress = product.PickupAddress;
            item.PickupCity = product.City;
            changed = true;
        }

        if (changed)
            await SaveCartToStorageAsync();
    }
}
