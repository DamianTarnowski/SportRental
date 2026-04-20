using SportRental.Shared.Models;

namespace SportRental.Shared.Services;

public interface IApiService
{
    // Konfiguracja API
    void SetBaseUrl(string baseUrl);
    void SetTenantId(Guid? tenantId);

    // Lokalizacje wypożyczalni
    Task<List<TenantLocationDto>> GetTenantLocationsAsync();

    // Produkty
    Task<List<ProductDto>> GetProductsAsync(int page = 1, int pageSize = 50);
    Task<ProductsPagedResponse> GetProductsPagedAsync(ProductFilterRequest filter);
    Task<ProductDto?> GetProductAsync(Guid id);

    // Klienci
    Task<CustomerDto> CreateCustomerAsync(CreateCustomerRequest request);
    Task<CustomerDto?> UpdateCustomerAsync(Guid id, CreateCustomerRequest request);
    Task<CustomerDto?> FindCustomerByEmailAsync(string email);
    Task<CustomerDto?> GetCustomerAsync(Guid id);

    // Platnosci
    Task<PaymentQuoteResponse> GetPaymentQuoteAsync(PaymentQuoteRequest request);
    Task<PaymentIntentDto> CreatePaymentIntentAsync(CreatePaymentIntentRequest request);
    Task<PaymentIntentDto?> GetPaymentIntentAsync(string id);
    Task<CheckoutSessionResponse> CreateCheckoutSessionAsync(CreateCheckoutSessionRequest request);
    Task<FinalizeSessionResponse?> FinalizeCheckoutSessionAsync(string sessionId);

    // Wynajmy
    Task<RentalResponse> CreateRentalAsync(CreateRentalRequest request);
    Task<bool> CancelRentalAsync(Guid rentalId);
    Task<string?> GetContractUrlAsync(Guid rentalId);
    Task<List<MyRentalDto>> GetMyRentalsAsync(string? status = null, DateTime? from = null, DateTime? to = null, Guid? customerId = null);

    // Holds (tymczasowe rezerwacje)
    Task<CreateHoldResponse?> CreateHoldAsync(CreateHoldRequest request);
    Task<bool> DeleteHoldAsync(Guid holdId, string? sessionId = null);

    // Guest session (bez rejestracji konta)
    Task<GuestSessionResult?> CreateGuestSessionAsync(GuestSessionPayload payload);

    // Opinie
    Task<RentalReviewDto?> PostRentalReviewAsync(CreateRentalReviewRequest request);
    Task<List<RentalReviewDto>> GetTenantReviewsAsync(Guid tenantId, int page = 1, int pageSize = 20);
    Task<ReviewSummaryDto> GetTenantReviewSummaryAsync(Guid tenantId);
}

public sealed class GuestSessionPayload
{
    public required string FullName { get; init; }
    public required string Email { get; init; }
    public string? PhoneNumber { get; init; }
    public string? Address { get; init; }
    public string? DocumentNumber { get; init; }
    public string? Notes { get; init; }
}

// SEC-009: access token trafia do HttpOnly cookie po stronie serwera,
// więc WASM go nie widzi i nie potrzebuje w tej odpowiedzi.
public sealed class GuestSessionResult
{
    public required int ExpiresIn { get; init; }
    public required Guid CustomerId { get; init; }
    public required string Email { get; init; }
    public required string FullName { get; init; }
}
