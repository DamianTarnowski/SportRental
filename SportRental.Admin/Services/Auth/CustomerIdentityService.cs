using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;

namespace SportRental.Admin.Services.Auth;

/// <summary>
/// Utrzymuje trwałe powiązanie konta Identity z profilem Customer. Adres email
/// nie jest dowodem własności historycznego profilu i nigdy nie służy do łączenia.
/// </summary>
public sealed class CustomerIdentityService(
    UserManager<ApplicationUser> userManager,
    IDbContextFactory<ApplicationDbContext> dbFactory)
{
    public async Task<Customer> GetOrCreateAsync(
        ApplicationUser user,
        string? fullName = null,
        string? phoneNumber = null,
        string? documentNumber = null,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var userClaims = await userManager.GetClaimsAsync(user);
        var customerClaim = userClaims.FirstOrDefault(claim => claim.Type == AuthClaims.CustomerId);

        if (Guid.TryParse(customerClaim?.Value, out var claimedCustomerId))
        {
            var claimedCustomer = await db.Customers.IgnoreQueryFilters()
                .FirstOrDefaultAsync(customer => customer.Id == claimedCustomerId, ct);
            if (claimedCustomer is not null)
                return claimedCustomer;

            var removeClaimResult = await userManager.RemoveClaimAsync(user, customerClaim!);
            if (!removeClaimResult.Succeeded)
            {
                throw new InvalidOperationException("Nie udało się usunąć nieaktualnego powiązania klienta.");
            }
        }

        var email = user.Email?.Trim().ToLowerInvariant() ?? string.Empty;
        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            TenantId = user.TenantId is { } tenantId && tenantId != Guid.Empty
                ? tenantId
                : Guid.Empty,
            FullName = string.IsNullOrWhiteSpace(fullName)
                ? (string.IsNullOrWhiteSpace(email) ? "Klient" : email.Split('@')[0])
                : fullName.Trim(),
            Email = email,
            PhoneNumber = phoneNumber?.Trim(),
            DocumentNumber = documentNumber?.Trim(),
            CreatedAtUtc = DateTime.UtcNow
        };

        db.Customers.Add(customer);
        await db.SaveChangesAsync(ct);

        var addClaimResult = await userManager.AddClaimAsync(
            user, new Claim(AuthClaims.CustomerId, customer.Id.ToString()));
        if (addClaimResult.Succeeded)
            return customer;

        // Nie zostawiaj osieroconego profilu, jeśli zapis trwałego powiązania Identity się nie udał.
        db.Customers.Remove(customer);
        await db.SaveChangesAsync(ct);
        throw new InvalidOperationException("Nie udało się zapisać powiązania konta z profilem klienta.");
    }
}
