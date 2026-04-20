using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SportRental.Shared.Models;

namespace SportRental.Shared.Services;

public class ApiService : IApiService
{
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
        try
        {
            var url = $"{_baseUrl}/api/tenants/locations";
            return await _httpClient.GetFromJsonAsync<List<TenantLocationDto>>(url, _jsonOptions) ?? new List<TenantLocationDto>();
        }
        catch (Exception)
        {
            return new List<TenantLocationDto>();
        }
    }

    public async Task<List<ProductDto>> GetProductsAsync(int page = 1, int pageSize = 50)
    {
        try
        {
            var url = $"{_baseUrl}/api/products?page={page}&pageSize={pageSize}";
            var response = await _httpClient.GetFromJsonAsync<ProductsPagedResponse>(url, _jsonOptions);
            return response?.Items ?? new List<ProductDto>();
        }
        catch (Exception)
        {
            return new List<ProductDto>();
        }
    }

    public async Task<ProductsPagedResponse> GetProductsPagedAsync(ProductFilterRequest filter)
    {
        try
        {
            var url = $"{_baseUrl}/api/products?{filter.ToQueryString()}";
            var response = await _httpClient.GetFromJsonAsync<ProductsPagedResponse>(url, _jsonOptions);
            return response ?? new ProductsPagedResponse();
        }
        catch (Exception)
        {
            return new ProductsPagedResponse();
        }
    }

    public async Task<ProductDto?> GetProductAsync(Guid id)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<ProductDto>($"{_baseUrl}/api/products/{id}", _jsonOptions);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<CustomerDto> CreateCustomerAsync(CreateCustomerRequest request)
    {
        var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/api/customers", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CustomerDto>()
            ?? throw new InvalidOperationException("Failed to create customer");
    }

    public async Task<CustomerDto?> UpdateCustomerAsync(Guid id, CreateCustomerRequest request)
    {
        var response = await _httpClient.PutAsJsonAsync($"{_baseUrl}/api/customers/{id}", request);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
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

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<CustomerDto>();
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<CustomerDto?> GetCustomerAsync(Guid id)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<CustomerDto>($"{_baseUrl}/api/customers/{id}", _jsonOptions);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<PaymentQuoteResponse> GetPaymentQuoteAsync(PaymentQuoteRequest request)
    {
        var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/api/payments/quote", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PaymentQuoteResponse>()
            ?? throw new InvalidOperationException("Failed to retrieve payment quote");
    }

    public async Task<PaymentIntentDto> CreatePaymentIntentAsync(CreatePaymentIntentRequest request)
    {
        var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/api/payments/intents", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PaymentIntentDto>()
            ?? throw new InvalidOperationException("Failed to create payment intent");
    }

    public async Task<PaymentIntentDto?> GetPaymentIntentAsync(string id)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            var response = await _httpClient.GetAsync($"{_baseUrl}/api/payments/intents/{id}");
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<PaymentIntentDto>();
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<CheckoutSessionResponse> CreateCheckoutSessionAsync(CreateCheckoutSessionRequest request)
    {
        var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/api/checkout/create-session", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CheckoutSessionResponse>()
            ?? throw new InvalidOperationException("Failed to create checkout session");
    }

    public async Task<FinalizeSessionResponse?> FinalizeCheckoutSessionAsync(string sessionId)
    {
        try
        {
            var response = await _httpClient.PostAsync($"{_baseUrl}/api/checkout/finalize-session/{sessionId}", null);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<FinalizeSessionResponse>();
            }
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
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RentalResponse>()
            ?? throw new InvalidOperationException("Failed to create rental");
    }

    public async Task<bool> CancelRentalAsync(Guid rentalId)
    {
        try
        {
            var response = await _httpClient.DeleteAsync($"{_baseUrl}/api/rentals/{rentalId}");
            return response.IsSuccessStatusCode;
        }
        catch (Exception)
        {
            return false;
        }
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
        try
        {
            var qp = new List<string>();
            if (!string.IsNullOrWhiteSpace(status)) qp.Add($"status={Uri.EscapeDataString(status)}");
            if (from.HasValue) qp.Add($"from={Uri.EscapeDataString(from.Value.ToString("o"))}");
            if (to.HasValue) qp.Add($"to={Uri.EscapeDataString(to.Value.ToString("o"))}");
            if (customerId.HasValue) qp.Add($"customerId={customerId.Value}");
            var url = $"{_baseUrl}/api/my-rentals" + (qp.Count > 0 ? "?" + string.Join("&", qp) : string.Empty);
            var list = await _httpClient.GetFromJsonAsync<List<MyRentalDto>>(url, _jsonOptions);
            return list ?? new List<MyRentalDto>();
        }
        catch (Exception)
        {
            return new List<MyRentalDto>();
        }
    }

    public async Task<CreateHoldResponse?> CreateHoldAsync(CreateHoldRequest request)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/api/holds", request);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"❌ CreateHold failed ({response.StatusCode}): {errorBody}");
                return null;
            }

            return await response.Content.ReadFromJsonAsync<CreateHoldResponse>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ CreateHold exception: {ex.Message}");
            return null;
        }
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

    public async Task<RentalReviewDto?> PostRentalReviewAsync(CreateRentalReviewRequest request)
    {
        var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/api/reviews", request);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }
        return await response.Content.ReadFromJsonAsync<RentalReviewDto>(_jsonOptions);
    }

    public async Task<List<RentalReviewDto>> GetTenantReviewsAsync(Guid tenantId, int page = 1, int pageSize = 20)
    {
        try
        {
            var url = $"{_baseUrl}/api/tenants/{tenantId}/reviews?page={page}&pageSize={pageSize}";
            var list = await _httpClient.GetFromJsonAsync<List<RentalReviewDto>>(url, _jsonOptions);
            return list ?? new List<RentalReviewDto>();
        }
        catch (Exception)
        {
            return new List<RentalReviewDto>();
        }
    }

    public async Task<ReviewSummaryDto> GetTenantReviewSummaryAsync(Guid tenantId)
    {
        try
        {
            var url = $"{_baseUrl}/api/tenants/{tenantId}/reviews/summary";
            var summary = await _httpClient.GetFromJsonAsync<ReviewSummaryDto>(url, _jsonOptions);
            return summary ?? new ReviewSummaryDto();
        }
        catch (Exception)
        {
            return new ReviewSummaryDto();
        }
    }
}
