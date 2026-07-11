using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SportRental.Admin.Data;
using SportRental.Admin.Services.Auth;
using SportRental.Infrastructure.Data;
using SportRental.Shared.Identity;

namespace SportRental.Admin.Services.Identity;

/// <summary>
/// Repairs public marketplace clients that an old startup seed accidentally
/// assigned to the default tenant and promoted to Owner. A real owner must have
/// a matching TenantUser Owner membership; a global Customer profile alone must
/// never grant staff access.
/// </summary>
public sealed class AccidentalOwnerPromotionRepair(
    UserManager<ApplicationUser> userManager,
    IDbContextFactory<ApplicationDbContext> dbFactory,
    ILogger<AccidentalOwnerPromotionRepair> logger)
{
    public async Task<int> RepairAsync(CancellationToken ct = default)
    {
        var ownerUsers = await userManager.GetUsersInRoleAsync(RoleNames.Owner);
        if (ownerUsers.Count == 0)
            return 0;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var repairedCount = 0;

        foreach (var user in ownerUsers)
        {
            ct.ThrowIfCancellationRequested();

            var claims = await userManager.GetClaimsAsync(user);
            var customerClaim = claims.FirstOrDefault(claim =>
                claim.Type == AuthClaims.CustomerId &&
                Guid.TryParse(claim.Value, out _));
            if (customerClaim is null || !Guid.TryParse(customerClaim.Value, out var customerId))
                continue;

            var isGlobalMarketplaceCustomer = await db.Customers
                .IgnoreQueryFilters()
                .AnyAsync(customer =>
                    customer.Id == customerId && customer.TenantId == Guid.Empty,
                    ct);
            if (!isGlobalMarketplaceCustomer)
                continue;

            var memberships = await db.TenantUsers
                .IgnoreQueryFilters()
                .Where(membership => membership.UserId == user.Id)
                .ToListAsync(ct);
            if (memberships.Any(membership =>
                    string.Equals(membership.Role, RoleNames.Owner, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var removeOwnerResult = await userManager.RemoveFromRoleAsync(user, RoleNames.Owner);
            if (!removeOwnerResult.Succeeded)
            {
                logger.LogError(
                    "Nie udało się usunąć przypadkowej roli Owner użytkownika {UserId}: {Errors}",
                    user.Id,
                    string.Join(", ", removeOwnerResult.Errors.Select(error => error.Code)));
                continue;
            }

            var retainedTenantId = memberships
                .Select(membership => membership.TenantId)
                .FirstOrDefault(tenantId => tenantId != Guid.Empty);
            user.TenantId = retainedTenantId == Guid.Empty ? null : retainedTenantId;

            var updateResult = await userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
            {
                logger.LogError(
                    "Usunięto przypadkową rolę Owner, ale nie udało się naprawić TenantId użytkownika {UserId}: {Errors}",
                    user.Id,
                    string.Join(", ", updateResult.Errors.Select(error => error.Code)));
            }

            repairedCount++;
        }

        if (repairedCount > 0)
        {
            logger.LogWarning(
                "Naprawiono {Count} kont klientów marketplace błędnie promowanych wcześniej do roli Owner.",
                repairedCount);
        }

        return repairedCount;
    }
}
