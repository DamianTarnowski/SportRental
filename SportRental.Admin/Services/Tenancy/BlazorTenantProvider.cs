using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using SportRental.Infrastructure.Tenancy;

namespace SportRental.Admin.Services.Tenancy;

/// <summary>
/// Tenant provider that works in both HTTP and Blazor Server (SignalR) contexts.
/// Priority: 1) authenticated claims, 2) anonymous catalog header,
/// 3) AuthenticationState claims for SignalR circuits.
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

        // Zalogowany użytkownik zawsze bierze tenant z podpisanego claimu. Nagłówek
        // X-Tenant-Id nie może nadpisać uprawnień Ownera/Employee.
        if (ctx?.User?.Identity?.IsAuthenticated == true)
        {
            var claim = ctx.User.FindFirst("tenant-id");
            if (claim != null && Guid.TryParse(claim.Value, out var tenantId) && tenantId != Guid.Empty)
                return tenantId;

            // Globalne konto klienta (Guid.Empty) celowo nie ma tenant scope.
            return null;
        }

        // Anonimowy nagłówek jest wyłącznie filtrem publicznego katalogu.
        if (ctx?.Request?.Headers != null &&
            ctx.Request.Headers.TryGetValue("X-Tenant-Id", out var headerValue))
        {
            var headerStr = headerValue.ToString();
            if (!string.IsNullOrWhiteSpace(headerStr) &&
                Guid.TryParse(headerStr, out var headerTenantId) &&
                headerTenantId != Guid.Empty)
            {
                return headerTenantId;
            }
        }

        // AuthenticationStateProvider is valid only inside a Razor component DI scope.
        // An anonymous HTTP request has no tenant claims by design, so do not use the
        // Blazor circuit fallback while HttpContext is available.
        if (ctx is not null)
            return null;

        // AuthenticationStateProvider (works during SignalR circuit in Blazor Server)
        try
        {
            var authState = _authStateProvider.GetAuthenticationStateAsync()
                .ConfigureAwait(false).GetAwaiter().GetResult();
            
            if (authState?.User?.Identity?.IsAuthenticated == true)
            {
                var claim = authState.User.FindFirst("tenant-id");
                if (claim != null && Guid.TryParse(claim.Value, out var tenantId) && tenantId != Guid.Empty)
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
