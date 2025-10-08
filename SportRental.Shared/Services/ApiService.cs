using System.Net;
using System.Net.Http.Json;
using SportRental.Shared.Models;

namespace SportRental.Shared.Services;

public class ApiService : IApiService
{
    private readonly HttpClient _httpClient;
    private string _baseUrl = string.Empty;
    private Guid? _tenantId;

    public ApiService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public void SetBaseUrl(string baseUrl)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _httpClient.BaseAddress = new Uri(_baseUrl);
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

    public async Task<List<ProductDto>> GetProductsAsync(int page = 1, int pageSize = 50)
    {
        try
        {
            var url = $"/api/products?page={page}&pageSize={pageSize}";
            var products = await _httpClient.GetFromJsonAsync<List<ProductDto>>(url);
            return products ?? new List<ProductDto>();
        }
        catch (Exception)
        {
            return new List<ProductDto>();
        }
    }

    public async Task<ProductDto?> GetProductAsync(Guid id)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<ProductDto>($"/api/products/{id}");
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<CustomerDto> CreateCustomerAsync(CreateCustomerRequest request)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/customers", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CustomerDto>()
            ?? throw new InvalidOperationException("Failed to create customer");
    }

    public async Task<CustomerDto?> UpdateCustomerAsync(Guid id, CreateCustomerRequest request)
    {
        var response = await _httpClient.PutAsJsonAsync($"/api/customers/{id}", request);
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
            var response = await _httpClient.GetAsync($"/api/customers/by-email?email={Uri.EscapeDataString(email)}");
            if (response.StatusCode == HttpStatusCode.NotFound)
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
            return await _httpClient.GetFromJsonAsync<CustomerDto>($"/api/customers/{id}");
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<PaymentQuoteResponse> GetPaymentQuoteAsync(PaymentQuoteRequest request)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/payments/quote", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PaymentQuoteResponse>()
            ?? throw new InvalidOperationException("Failed to retrieve payment quote");
    }

    public async Task<PaymentIntentDto> CreatePaymentIntentAsync(CreatePaymentIntentRequest request)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/payments/intents", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PaymentIntentDto>()
            ?? throw new InvalidOperationException("Failed to create payment intent");
    }

    public async Task<PaymentIntentDto?> GetPaymentIntentAsync(Guid id)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/api/payments/intents/{id}");
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
        var response = await _httpClient.PostAsJsonAsync("/api/checkout/create-session", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CheckoutSessionResponse>()
            ?? throw new InvalidOperationException("Failed to create checkout session");
    }

    public async Task<RentalResponse> CreateRentalAsync(CreateRentalRequest request)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/rentals", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RentalResponse>()
            ?? throw new InvalidOperationException("Failed to create rental");
    }

    public async Task<bool> CancelRentalAsync(Guid rentalId)
    {
        try
        {
            var response = await _httpClient.DeleteAsync($"/api/rentals/{rentalId}");
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
            var response = await _httpClient.GetAsync($"/api/contracts/{rentalId}");
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
            var url = "/api/my-rentals" + (qp.Count > 0 ? "?" + string.Join("&", qp) : string.Empty);
            var list = await _httpClient.GetFromJsonAsync<List<MyRentalDto>>(url);
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
            var response = await _httpClient.PostAsJsonAsync("/api/holds", request);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadFromJsonAsync<CreateHoldResponse>();
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<bool> DeleteHoldAsync(Guid holdId)
    {
        try
        {
            var response = await _httpClient.DeleteAsync($"/api/holds/{holdId}");
            return response.IsSuccessStatusCode;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

