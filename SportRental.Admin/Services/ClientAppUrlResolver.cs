namespace SportRental.Admin.Services;

/// <summary>
/// Resolves the public root of the customer WASM application. In production the
/// client is bundled into Admin under /_client; an explicitly configured URL
/// remains authoritative for standalone hosting.
/// </summary>
public static class ClientAppUrlResolver
{
    public static string Resolve(IConfiguration configuration, string? adminBaseUrl)
    {
        var configuredClientUrl = configuration["ClientApp:PublicBaseUrl"];
        if (!string.IsNullOrWhiteSpace(configuredClientUrl))
        {
            return configuredClientUrl.TrimEnd('/');
        }

        var normalizedAdminUrl = adminBaseUrl?.TrimEnd('/');
        return string.IsNullOrWhiteSpace(normalizedAdminUrl)
            ? string.Empty
            : $"{normalizedAdminUrl}/_client";
    }

    /// <summary>
    /// Resolves an origin that is safe to embed in security-sensitive links.
    /// Production never falls back to Request.Host: the public URL must come
    /// from trusted configuration. HTTP is accepted only for loopback in dev.
    /// </summary>
    public static bool TryResolveSecurityBaseUrl(
        IConfiguration configuration,
        IHostEnvironment environment,
        out string baseUrl)
    {
        var candidate = configuration["ClientApp:PublicBaseUrl"];
        if (string.IsNullOrWhiteSpace(candidate))
        {
            var adminBaseUrl = configuration["Admin:PublicBaseUrl"];
            if (!string.IsNullOrWhiteSpace(adminBaseUrl))
                candidate = $"{adminBaseUrl.TrimEnd('/')}/_client";
        }

        if (string.IsNullOrWhiteSpace(candidate) && environment.IsDevelopment())
            candidate = "http://localhost:5014";

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            baseUrl = string.Empty;
            return false;
        }

        var usesHttps = string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        var usesLocalDevelopmentHttp = environment.IsDevelopment() &&
                                       uri.IsLoopback &&
                                       string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);
        if (!usesHttps && !usesLocalDevelopmentHttp)
        {
            baseUrl = string.Empty;
            return false;
        }

        baseUrl = uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
        return true;
    }
}
