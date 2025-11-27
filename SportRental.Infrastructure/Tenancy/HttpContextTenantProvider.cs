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

        // Priority 1: X-Tenant-Id header (for API calls from client apps)
        if (ctx.Request.Headers.TryGetValue("X-Tenant-Id", out var headerValue))
        {
            var headerStr = headerValue.ToString();
            if (!string.IsNullOrWhiteSpace(headerStr) && Guid.TryParse(headerStr, out var headerTenantId))
                return headerTenantId;
        }

        // Priority 2: Authenticated user claim (for logged-in users)
        if (ctx.User?.Identity?.IsAuthenticated == true)
        {
            var claim = ctx.User.FindFirst("tenant-id");
            if (claim != null && Guid.TryParse(claim.Value, out var tenantId))
                return tenantId;
        }

        // Priority 3: Configuration fallback (for development/testing)
        var configuredId = _configuration["Tenant:Id"];
        if (!string.IsNullOrWhiteSpace(configuredId) && Guid.TryParse(configuredId, out var cfgTenant))
            return cfgTenant;

        // Fallback: null (will return all data if endpoint allows it)
        return null;
    }
}