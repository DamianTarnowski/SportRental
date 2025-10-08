using SportRental.Shared.Models;

namespace SportRental.Shared.Services;

public interface IApiService
{
    // Konfiguracja API
    void SetBaseUrl(string baseUrl);
    void SetTenantId(Guid? tenantId);

    // Produkty
    Task<List<ProductDto>> GetProductsAsync(int page = 1, int pageSize = 50);
    Task<ProductDto?> GetProductAsync(Guid id);

    // Klienci
    Task<CustomerDto> CreateCustomerAsync(CreateCustomerRequest request);
    Task<CustomerDto?> UpdateCustomerAsync(Guid id, CreateCustomerRequest request);
    Task<CustomerDto?> FindCustomerByEmailAsync(string email);
    Task<CustomerDto?> GetCustomerAsync(Guid id);

    // Platnosci
    Task<PaymentQuoteResponse> GetPaymentQuoteAsync(PaymentQuoteRequest request);
    Task<PaymentIntentDto> CreatePaymentIntentAsync(CreatePaymentIntentRequest request);
    Task<PaymentIntentDto?> GetPaymentIntentAsync(Guid id);
    Task<CheckoutSessionResponse> CreateCheckoutSessionAsync(CreateCheckoutSessionRequest request);

    // Wynajmy
    Task<RentalResponse> CreateRentalAsync(CreateRentalRequest request);
    Task<bool> CancelRentalAsync(Guid rentalId);
    Task<string?> GetContractUrlAsync(Guid rentalId);
    Task<List<MyRentalDto>> GetMyRentalsAsync(string? status = null, DateTime? from = null, DateTime? to = null, Guid? customerId = null);

    // Holds (tymczasowe rezerwacje)
    Task<CreateHoldResponse?> CreateHoldAsync(CreateHoldRequest request);
    Task<bool> DeleteHoldAsync(Guid holdId);
}
