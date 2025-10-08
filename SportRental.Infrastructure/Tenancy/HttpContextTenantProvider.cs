using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace SportRental.Infrastructure.Tenancy;

public class HttpContextTenantProvider(IHttpContextAccessor httpContextAccessor, IConfiguration configuration) : ITenantProvider
{
    private readonly IHttpContextAccessor _http = httpContextAccessor;
    private readonly IConfiguration _configuration = configuration;

    public Guid? GetCurrentTenantId()
    {
        var ctx = _http.HttpContext;
        if (ctx == null) return null;

        // Przykład: z nagłówka, subdomeny lub roszczeń użytkownika
        if (ctx.User?.Identity?.IsAuthenticated == true)
        {
            var claim = ctx.User.FindFirst("tenant-id");
            if (claim != null && Guid.TryParse(claim.Value, out var tenantId))
                return tenantId;
        }

        // Fallback: z konfiguracji (np. User Secrets) Tenant:Id
        var configuredId = _configuration["Tenant:Id"];
        if (!string.IsNullOrWhiteSpace(configuredId) && Guid.TryParse(configuredId, out var cfgTenant))
            return cfgTenant;

        // Fallback: null
        return null;
    }
}