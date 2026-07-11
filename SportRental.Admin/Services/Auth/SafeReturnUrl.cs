namespace SportRental.Admin.Services.Auth;

/// <summary>
/// Normalizes return URLs before they are persisted in an authentication flow
/// or passed to a redirect response. Only same-origin, root-relative paths are
/// accepted; protocol-relative and ambiguous encoded-slash paths are rejected.
/// </summary>
public static class SafeReturnUrl
{
    public const string ClientFallback = "/_client/";

    public static string ResolveLocal(string? returnUrl, string fallback = "/")
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
            return fallback;

        var candidate = returnUrl.Trim();
        if (candidate.Any(char.IsControl) ||
            candidate.Contains('\\') ||
            candidate.StartsWith("//", StringComparison.Ordinal) ||
            candidate.StartsWith("/%2f", StringComparison.OrdinalIgnoreCase) ||
            candidate.StartsWith("/%5c", StringComparison.OrdinalIgnoreCase))
        {
            return fallback;
        }

        if (candidate.StartsWith("~/", StringComparison.Ordinal))
            candidate = candidate[1..];

        if (!candidate.StartsWith("/", StringComparison.Ordinal))
        {
            if (!Uri.IsWellFormedUriString(candidate, UriKind.Relative) ||
                Uri.TryCreate(candidate, UriKind.Absolute, out _))
            {
                return fallback;
            }

            candidate = $"/{candidate}";
        }

        if (candidate.StartsWith("//", StringComparison.Ordinal) ||
            !Uri.IsWellFormedUriString(candidate, UriKind.Relative))
        {
            return fallback;
        }

        return candidate;
    }

    public static string ResolveClient(string? returnUrl)
    {
        var candidate = ResolveLocal(returnUrl, ClientFallback);
        return IsClientPath(candidate) ? candidate : ClientFallback;
    }

    public static bool IsClientPath(string? returnUrl)
    {
        if (string.IsNullOrEmpty(returnUrl) ||
            !returnUrl.StartsWith("/_client", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return returnUrl.Length == "/_client".Length ||
               returnUrl["/_client".Length] is '/' or '?' or '#';
    }
}
