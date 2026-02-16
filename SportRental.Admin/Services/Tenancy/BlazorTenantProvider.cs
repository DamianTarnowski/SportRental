using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using SportRental.Infrastructure.Tenancy;

namespace SportRental.Admin.Services.Tenancy;

/// <summary>
/// Tenant provider that works in both HTTP and Blazor Server (SignalR) contexts.
/// Priority: 1) X-Tenant-Id header, 2) HttpContext claims, 3) AuthenticationState claims (for SignalR circuits).
/// </summary>
public class BlazorTenantProvider : ITenantProvider
{
    private readonly IHttpContextAccessor _http;
    private readonly AuthenticationStateProvider _authStateProvider;

    public BlazorTenantProvider(IHttpContextAccessor http, AuthenticationStateProvider authStateProvider)
    {
        _http = http;
        _authStateProvider = authStateProvider;
    }

    public Guid? GetCurrentTenantId()
    {
        var ctx = _http.HttpContext;

        // Priority 1: X-Tenant-Id header (for API calls from client apps)
        if (ctx?.Request?.Headers != null &&
            ctx.Request.Headers.TryGetValue("X-Tenant-Id", out var headerValue))
        {
            var headerStr = headerValue.ToString();
            if (!string.IsNullOrWhiteSpace(headerStr) && Guid.TryParse(headerStr, out var headerTenantId))
                return headerTenantId;
        }

        // Priority 2: HttpContext claims (works during initial HTTP request)
        if (ctx?.User?.Identity?.IsAuthenticated == true)
        {
            var claim = ctx.User.FindFirst("tenant-id");
            if (claim != null && Guid.TryParse(claim.Value, out var tenantId))
                return tenantId;
        }

        // Priority 3: AuthenticationStateProvider (works during SignalR circuit in Blazor Server)
        try
        {
            var authState = _authStateProvider.GetAuthenticationStateAsync()
                .ConfigureAwait(false).GetAwaiter().GetResult();
            
            if (authState?.User?.Identity?.IsAuthenticated == true)
            {
                var claim = authState.User.FindFirst("tenant-id");
                if (claim != null && Guid.TryParse(claim.Value, out var tenantId))
                    return tenantId;
            }
        }
        catch
        {
            // AuthenticationStateProvider may not be available in all contexts
        }

        return null;
    }
}
