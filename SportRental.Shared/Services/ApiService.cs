using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SportRental.Shared.Legal;
using SportRental.Shared.Models;

namespace SportRental.Shared.Services;

public class ApiService : IApiService
{
    public string? LastHoldError { get; private set; }
    private readonly HttpClient _httpClient;
    private string _baseUrl = string.Empty;
    private Guid? _tenantId;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ApiService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public void SetBaseUrl(string baseUrl)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        // NIE ustawiamy BaseAddress - używamy pełnych URL w metodach
        // HttpClient.BaseAddress można ustawić tylko raz, więc lepiej tego unikać
    }

    public void SetTenantId(Guid? tenantId)
    {
        _tenantId = tenantId;
        _httpClient.DefaultRequestHeaders.Remove("X-Tenant-Id");
        if (tenantId.HasValue)
        {
            _httpClient.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId.Value.ToString());
        }
    }

    public async Task<List<TenantLocationDto>> GetTenantLocationsAsync()
    {
        var url = $"{_baseUrl}/api/tenants/locations";
        return await _httpClient.GetFromJsonAsync<List<TenantLocationDto>>(url, _jsonOptions)
            ?? new List<TenantLocationDto>();
    }

    public async Task<LegalInfoDto> GetLegalInfoAsync()
    {
        return await _httpClient.GetFromJsonAsync<LegalInfoDto>(
                   $"{_baseUrl}/api/legal/info",
                   _jsonOptions)
               ?? new LegalInfoDto();
    }

    public async Task SendContactMessageAsync(ContactMessageRequest request)
    {
        var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/api/contact", request);
        await EnsureApiSuccessAsync(response);
    }

    public async Task<List<ProductDto>> GetProductsAsync(int page = 1, int pageSize = 50)
    {
        var url = $"{_baseUrl}/api/products?page={page}&pageSize={pageSize}";
        var response = await _httpClient.GetFromJsonAsync<ProductsPagedResponse>(url, _jsonOptions);
        return response?.Items ?? new List<ProductDto>();
    }

    public async Task<ProductsPagedResponse> GetProductsPagedAsync(ProductFilterRequest filter)
    {
        var url = $"{_baseUrl}/api/products?{filter.ToQueryString()}";
        var response = await _httpClient.GetFromJsonAsync<ProductsPagedResponse>(url, _jsonOptions);
        return response ?? new ProductsPagedResponse();
    }

    public async Task<ProductCatalogFacetsDto> GetProductCatalogFacetsAsync()
    {
        return await _httpClient.GetFromJsonAsync<ProductCatalogFacetsDto>(
                   $"{_baseUrl}/api/products/facets",
                   _jsonOptions)
               ?? new ProductCatalogFacetsDto();
    }

    public async Task<ProductDto?> GetProductAsync(Guid id)
    {
        var response = await _httpClient.GetAsync($"{_baseUrl}/api/products/{id}");
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        await EnsureApiSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<ProductDto>(_jsonOptions);
    }

    public async Task<CustomerDto?> UpdateCustomerAsync(Guid id, CreateCustomerRequest request)
    {
        var response = await _httpClient.PutAsJsonAsync($"{_baseUrl}/api/customers/{id}", request);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureApiSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<CustomerDto>()
            ?? throw new InvalidOperationException("Failed to update customer");
    }

    public async Task<CustomerDto?> FindCustomerByEmailAsync(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/api/customers/by-email?email={Uri.EscapeDataString(email)}");
            // 401 = anonimowy wywołujący; 404 = brak/nieuprawniony. Zwracamy null, nie lookuje cudzych.
            if (response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return null;
            }

            await EnsureApiSuccessAsync(response);
            return await response.Content.ReadFromJsonAsync<CustomerDto>();
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<CustomerDto?> GetCustomerAsync(Guid id)
    {
        var response = await _httpClient.GetAsync($"{_baseUrl}/api/customers/{id}");
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        await EnsureApiSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<CustomerDto>(_jsonOptions);
    }

    public async Task<PaymentQuoteResponse> GetPaymentQuoteAsync(PaymentQuoteRequest request)
    {
        var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/api/payments/quote", request);
        await EnsureApiSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<PaymentQuoteResponse>()
            ?? throw new InvalidOperationException("Failed to retrieve payment quote");
    }

    public async Task<CheckoutSessionResponse> CreateCheckoutSessionAsync(CreateCheckoutSessionRequest request)
    {
        var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/api/checkout/create-session", request);
        await EnsureApiSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<CheckoutSessionResponse>()
            ?? throw new InvalidOperationException("Failed to create checkout session");
    }

    public async Task<FinalizeSessionResponse?> FinalizeCheckoutSessionAsync(string sessionId)
    {
        try
        {
            var response = await _httpClient.PostAsync($"{_baseUrl}/api/checkout/finalize-session/{sessionId}", null);
            var result = await response.Content.ReadFromJsonAsync<FinalizeSessionResponse>(_jsonOptions);
            if (result is not null)
                return result;
            await EnsureApiSuccessAsync(response);
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<RentalResponse> CreateRentalAsync(CreateRentalRequest request)
    {
        var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/api/rentals", request);
        await EnsureApiSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<RentalResponse>()
            ?? throw new InvalidOperationException("Failed to create rental");
    }

    public async Task<bool> CancelRentalAsync(Guid rentalId)
    {
        var response = await _httpClient.DeleteAsync($"{_baseUrl}/api/rentals/{rentalId}");
        await EnsureApiSuccessAsync(response);
        return true;
    }

    public async Task<string?> GetContractUrlAsync(Guid rentalId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/api/contracts/{rentalId}");
            if (response.IsSuccessStatusCode)
            {
                return response.Headers.Location?.ToString();
            }

            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<List<MyRentalDto>> GetMyRentalsAsync(string? status = null, DateTime? from = null, DateTime? to = null, Guid? customerId = null)
    {
        var qp = new List<string>();
        if (!string.IsNullOrWhiteSpace(status)) qp.Add($"status={Uri.EscapeDataString(status)}");
        if (from.HasValue) qp.Add($"from={Uri.EscapeDataString(from.Value.ToString("o"))}");
        if (to.HasValue) qp.Add($"to={Uri.EscapeDataString(to.Value.ToString("o"))}");
        var url = $"{_baseUrl}/api/my-rentals" + (qp.Count > 0 ? "?" + string.Join("&", qp) : string.Empty);
        var list = await _httpClient.GetFromJsonAsync<List<MyRentalDto>>(url, _jsonOptions);
        return list ?? new List<MyRentalDto>();
    }

    public async Task<CreateHoldResponse?> CreateHoldAsync(CreateHoldRequest request)
    {
        LastHoldError = null;
        try
        {
            var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/api/holds", request);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                LastHoldError = ReadSafeError(errorBody)
                    ?? "Nie udało się zarezerwować produktu dla wybranego terminu.";
                Console.WriteLine($"CreateHold failed ({response.StatusCode}): {LastHoldError}");
                return null;
            }

            return await response.Content.ReadFromJsonAsync<CreateHoldResponse>();
        }
        catch (Exception ex)
        {
            LastHoldError = "Nie udało się połączyć z serwerem rezerwacji.";
            Console.WriteLine($"CreateHold exception: {ex.GetType().Name}");
            return null;
        }
    }

    private static string? ReadSafeError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        try
        {
            using var json = System.Text.Json.JsonDocument.Parse(body);
            foreach (var propertyName in new[] { "error", "message" })
            {
                if (json.RootElement.TryGetProperty(propertyName, out var value) &&
                    value.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    var message = value.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(message) && message.Length <= 300)
                        return message;
                }
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Nie pokazujemy klientowi surowego HTML ani treści błędu infrastruktury.
        }

        return null;
    }

    public async Task<bool> DeleteHoldAsync(Guid holdId, string? sessionId = null)
    {
        try
        {
            var url = $"{_baseUrl}/api/holds/{holdId}";
            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                url += $"?sessionId={Uri.EscapeDataString(sessionId)}";
            }
            var response = await _httpClient.DeleteAsync(url);
            return response.IsSuccessStatusCode;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<CreateHoldResponse?> RefreshHoldAsync(Guid holdId, string sessionId, int ttlMinutes = 10)
    {
        try
        {
            var url = $"{_baseUrl}/api/holds/{holdId}/refresh?sessionId={Uri.EscapeDataString(sessionId)}&ttlMinutes={ttlMinutes}";
            var response = await _httpClient.PostAsync(url, content: null);
            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync<CreateHoldResponse>(_jsonOptions);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<GuestSessionResult?> CreateGuestSessionAsync(GuestSessionPayload payload)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/api/auth/guest-session", new
            {
                payload.FullName,
                payload.Email,
                payload.PhoneNumber,
                payload.Address,
                payload.DocumentNumber,
                payload.Notes
            });
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }
            return await response.Content.ReadFromJsonAsync<GuestSessionResult>(_jsonOptions);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<bool> RequestGuestOrderAccessAsync(GuestOrderAccessRequest request)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                $"{_baseUrl}/api/auth/guest-order-access/request",
                request);
            return response.IsSuccessStatusCode;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<GuestSessionResult?> RedeemGuestOrderAccessAsync(string token)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                $"{_baseUrl}/api/auth/guest-order-access/redeem",
                new { Token = token });
            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync<GuestSessionResult>(_jsonOptions);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<RentalReviewDto?> PostRentalReviewAsync(CreateRentalReviewRequest request)
    {
        var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/api/reviews", request);
        await EnsureApiSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<RentalReviewDto>(_jsonOptions);
    }

    public async Task<List<RentalReviewDto>> GetTenantReviewsAsync(Guid tenantId, int page = 1, int pageSize = 20)
    {
        var url = $"{_baseUrl}/api/tenants/{tenantId}/reviews?page={page}&pageSize={pageSize}";
        var list = await _httpClient.GetFromJsonAsync<List<RentalReviewDto>>(url, _jsonOptions);
        return list ?? new List<RentalReviewDto>();
    }

    public async Task<ReviewSummaryDto> GetTenantReviewSummaryAsync(Guid tenantId)
    {
        var url = $"{_baseUrl}/api/tenants/{tenantId}/reviews/summary";
        var summary = await _httpClient.GetFromJsonAsync<ReviewSummaryDto>(url, _jsonOptions);
        return summary ?? new ReviewSummaryDto();
    }

    public async Task<List<RentalReviewDto>> GetReviewsAsync(int page = 1, int pageSize = 20)
    {
        var url = $"{_baseUrl}/api/reviews?page={page}&pageSize={pageSize}";
        var list = await _httpClient.GetFromJsonAsync<List<RentalReviewDto>>(url, _jsonOptions);
        return list ?? new List<RentalReviewDto>();
    }

    public async Task<ReviewSummaryDto> GetReviewSummaryAsync()
    {
        var url = $"{_baseUrl}/api/reviews/summary";
        var summary = await _httpClient.GetFromJsonAsync<ReviewSummaryDto>(url, _jsonOptions);
        return summary ?? new ReviewSummaryDto();
    }

    private static async Task EnsureApiSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;

        var message = $"API zwróciło {(int)response.StatusCode} {response.ReasonPhrase}.";
        try
        {
            var body = await response.Content.ReadAsStringAsync();
            if (!string.IsNullOrWhiteSpace(body))
            {
                using var json = JsonDocument.Parse(body);
                if (json.RootElement.TryGetProperty("error", out var error) ||
                    json.RootElement.TryGetProperty("message", out error))
                {
                    message = error.GetString() ?? message;
                }
            }
        }
        catch (JsonException)
        {
            // Keep the status-based fallback for non-JSON responses.
        }

        throw new HttpRequestException(message, inner: null, response.StatusCode);
    }
}
