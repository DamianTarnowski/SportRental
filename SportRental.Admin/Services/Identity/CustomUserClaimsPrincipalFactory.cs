using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using SportRental.Infrastructure.Domain;

namespace SportRental.Admin.Services.Identity;

/// <summary>
/// Adds custom claims (tenant-id) to user's ClaimsPrincipal on login
/// </summary>
public class CustomUserClaimsPrincipalFactory : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole<Guid>>
{
    public CustomUserClaimsPrincipalFactory(
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole<Guid>> roleManager,
        IOptions<IdentityOptions> optionsAccessor)
        : base(userManager, roleManager, optionsAccessor)
    {
    }

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        
        // Add tenant-id claim if user has TenantId
        if (user.TenantId.HasValue && user.TenantId.Value != Guid.Empty)
        {
            identity.AddClaim(new Claim("tenant-id", user.TenantId.Value.ToString()));
        }
        
        return identity;
    }
}

