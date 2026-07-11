using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace SportRental.Admin.Services.Contracts;

public interface IContractAccessLinkService
{
    string CreatePath(Guid rentalId);
    bool TryResolveRentalId(string token, out Guid rentalId);
}

/// <summary>
/// Tworzy nieprzewidywalny, integralny token do anonimowego odczytu umowy.
/// Token jest związany z key ringiem aplikacji, więc przeżywa restart i rotację kluczy.
/// </summary>
public sealed class ContractAccessLinkService : IContractAccessLinkService
{
    private const string Purpose = "RentSpot.ContractAccess.v1";
    private readonly IDataProtector _protector;

    public ContractAccessLinkService(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector(Purpose);
    }

    public string CreatePath(Guid rentalId)
    {
        if (rentalId == Guid.Empty)
            throw new ArgumentException("Rental id cannot be empty.", nameof(rentalId));

        return $"/c/{_protector.Protect(rentalId.ToString("N"))}";
    }

    public bool TryResolveRentalId(string token, out Guid rentalId)
    {
        rentalId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(token) || token.Length > 2048)
            return false;

        try
        {
            var value = _protector.Unprotect(token);
            return Guid.TryParseExact(value, "N", out rentalId) && rentalId != Guid.Empty;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
