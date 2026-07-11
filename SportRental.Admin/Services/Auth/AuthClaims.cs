using System.Security.Claims;
using SportRental.Shared.Identity;

namespace SportRental.Admin.Services.Auth;

public static class AuthClaims
{
    public const string CustomerId = "customer-id";
    public const string TenantId = "tenant-id";

    public static Guid? GetCustomerId(this ClaimsPrincipal user)
    {
        var value = user.FindFirstValue(CustomerId);
        return Guid.TryParse(value, out var id) && id != Guid.Empty ? id : null;
    }

    public static Guid? GetTenantId(this ClaimsPrincipal user)
    {
        var value = user.FindFirstValue(TenantId);
        return Guid.TryParse(value, out var id) && id != Guid.Empty ? id : null;
    }

    public static bool IsAdmin(this ClaimsPrincipal user) =>
        user.IsInRole(RoleNames.Owner) ||
        user.IsInRole(RoleNames.Employee) ||
        user.IsInRole(RoleNames.SuperAdmin) ||
        // Legacy role names retained for accounts created by older deployments.
        user.IsInRole("Admin") ||
        user.IsInRole("OwnerAdmin");
}
