using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SportRental.Admin.Services.Auth;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;

namespace SportRental.Admin.Services.Identity;

/// <summary>
/// Adds custom claims (tenant-id, customer-id) to user's ClaimsPrincipal on login.
/// customer-id is resolved by matching email + tenantId against the Customer table.
/// </summary>
public class CustomUserClaimsPrincipalFactory : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole<Guid>>
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;

    public CustomUserClaimsPrincipalFactory(
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole<Guid>> roleManager,
        IOptions<IdentityOptions> optionsAccessor,
        IDbContextFactory<ApplicationDbContext> dbFactory)
        : base(userManager, roleManager, optionsAccessor)
    {
        _dbFactory = dbFactory;
    }

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);

        if (user.TenantId.HasValue && user.TenantId.Value != Guid.Empty)
        {
            identity.AddClaim(new Claim(AuthClaims.TenantId, user.TenantId.Value.ToString()));
        }

        var customerId = await ResolveCustomerIdAsync(user);
        if (customerId.HasValue)
        {
            identity.AddClaim(new Claim(AuthClaims.CustomerId, customerId.Value.ToString()));
        }

        return identity;
    }

    private async Task<Guid?> ResolveCustomerIdAsync(ApplicationUser user)
    {
        if (string.IsNullOrWhiteSpace(user.Email)) return null;

        await using var db = await _dbFactory.CreateDbContextAsync();
        var email = user.Email.Trim().ToLower();

        var query = db.Customers.IgnoreQueryFilters()
            .Where(c => c.Email != null && c.Email.ToLower() == email);

        if (user.TenantId.HasValue && user.TenantId.Value != Guid.Empty)
        {
            query = query.Where(c => c.TenantId == user.TenantId.Value);
        }

        var customer = await query
            .OrderBy(c => c.CreatedAtUtc)
            .FirstOrDefaultAsync();

        return customer?.Id;
    }
}
