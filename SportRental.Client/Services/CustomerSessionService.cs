using System.Text.Json;
using Microsoft.JSInterop;
using SportRental.Shared.Models;

namespace SportRental.Client.Services;

public interface ICustomerSessionService
{
    event EventHandler? SessionChanged;
    Task<CustomerDto?> GetCustomerAsync();
    Task SetCustomerAsync(CustomerDto customer);
    Task ClearAsync();
}

public class CustomerSessionService : ICustomerSessionService
{
    private const string SessionStorageKey = "sport-rental-customer";
    private readonly IJSRuntime _jsRuntime;
    private readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private CustomerDto? _cached;
    private bool _isLoaded;

    public event EventHandler? SessionChanged;

    public CustomerSessionService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    public async Task<CustomerDto?> GetCustomerAsync()
    {
        await EnsureLoadedAsync();
        return _cached;
    }

    public async Task SetCustomerAsync(CustomerDto customer)
    {
        await EnsureLoadedAsync();
        _cached = customer;
        var json = JsonSerializer.Serialize(customer, _serializerOptions);
        await _jsRuntime.InvokeVoidAsync("localStorage.setItem", SessionStorageKey, json);
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task ClearAsync()
    {
        await EnsureLoadedAsync();
        _cached = null;
        await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", SessionStorageKey);
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task EnsureLoadedAsync()
    {
        if (_isLoaded)
        {
            return;
        }

        try
        {
            var json = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", SessionStorageKey);
            if (!string.IsNullOrWhiteSpace(json))
            {
                var sanitized = json.TrimStart('\ufeff', '\u200b');
                if (!string.IsNullOrWhiteSpace(sanitized))
                {
                    _cached = JsonSerializer.Deserialize<CustomerDto>(sanitized, _serializerOptions);
                }
            }
        }
        catch
        {
            _cached = null;
        }

        _isLoaded = true;
    }
}
