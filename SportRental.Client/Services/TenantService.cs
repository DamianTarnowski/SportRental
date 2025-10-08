using Blazored.LocalStorage;
using System.Net.Http.Json;

namespace SportRental.Client.Services;

public class TenantService
{
    private readonly HttpClient _httpClient;
    private readonly ILocalStorageService _localStorage;
    private const string TENANT_KEY = "selected_tenant_id";
    private const string TENANT_NAME_KEY = "selected_tenant_name";

    public TenantService(HttpClient httpClient, ILocalStorageService localStorage)
    {
        _httpClient = httpClient;
        _localStorage = localStorage;
    }

    public async Task<List<TenantInfo>> GetAvailableTenantsAsync()
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<List<TenantInfo>>("/api/tenants");
            return response ?? new List<TenantInfo>();
        }
        catch (Exception)
        {
            return new List<TenantInfo>();
        }
    }

    public async Task<string?> GetSelectedTenantIdAsync()
    {
        return await _localStorage.GetItemAsync<string>(TENANT_KEY);
    }

    public async Task<string?> GetSelectedTenantNameAsync()
    {
        return await _localStorage.GetItemAsync<string>(TENANT_NAME_KEY);
    }

    public async Task SetSelectedTenantAsync(string tenantId, string tenantName)
    {
        await _localStorage.SetItemAsync(TENANT_KEY, tenantId);
        await _localStorage.SetItemAsync(TENANT_NAME_KEY, tenantName);
    }

    public async Task ClearSelectedTenantAsync()
    {
        await _localStorage.RemoveItemAsync(TENANT_KEY);
        await _localStorage.RemoveItemAsync(TENANT_NAME_KEY);
    }
}

public class TenantInfo
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public string? PrimaryColor { get; set; }
    public string? SecondaryColor { get; set; }
}
