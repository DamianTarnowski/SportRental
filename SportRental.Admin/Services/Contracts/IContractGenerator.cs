using SportRental.Infrastructure.Domain;

namespace SportRental.Admin.Services.Contracts
{
    public interface IContractGenerator
    {
        Task<byte[]> GenerateRentalContractAsync(Rental rental, IEnumerable<RentalItem> items, Customer customer, IEnumerable<Product> products, CompanyInfo? companyInfo = null, CancellationToken ct = default);
        Task<byte[]> GenerateRentalContractAsync(string templateContent, Rental rental, IEnumerable<RentalItem> items, Customer customer, IEnumerable<Product> products, CompanyInfo? companyInfo = null, CancellationToken ct = default);
        Task<string> GenerateAndSaveRentalContractAsync(Rental rental, IEnumerable<RentalItem> items, Customer customer, IEnumerable<Product> products, CompanyInfo? companyInfo = null, CancellationToken ct = default);
        Task SendRentalContractByEmailAsync(Rental rental, IEnumerable<RentalItem> items, Customer customer, IEnumerable<Product> products, CompanyInfo? companyInfo = null, CancellationToken ct = default);
        
        /// <summary>
        /// Wysyła email z potwierdzeniem wynajmu i załącznikiem PDF umowy
        /// </summary>
        Task SendRentalConfirmationEmailAsync(Rental rental, IEnumerable<RentalItem> items, Customer customer, IEnumerable<Product> products, CompanyInfo? companyInfo = null, CancellationToken ct = default);
    }
}


