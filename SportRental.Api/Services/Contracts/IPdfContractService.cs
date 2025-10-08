using SportRental.Infrastructure.Domain;

namespace SportRental.Api.Services.Contracts;

/// <summary>
/// Service for generating rental contracts as PDF
/// </summary>
public interface IPdfContractService
{
    /// <summary>
    /// Generate rental contract PDF
    /// </summary>
    Task<byte[]> GenerateContractPdfAsync(
        Rental rental,
        Customer customer,
        List<(Product product, int quantity)> items,
        CompanyInfo? companyInfo = null);

    /// <summary>
    /// Generate contract PDF and save to disk
    /// </summary>
    Task<string> GenerateAndSaveContractPdfAsync(
        Rental rental,
        Customer customer,
        List<(Product product, int quantity)> items,
        CompanyInfo? companyInfo = null);
}
