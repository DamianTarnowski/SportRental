namespace SportRental.Client.Services;

public static class SafeClientNavigation
{
    public static string NormalizeRelativeReturnUrl(string? returnUrl, string fallback = "./")
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
            return fallback;

        var candidate = returnUrl.Trim();
        if (candidate.StartsWith('/') ||
            candidate.StartsWith('\\') ||
            candidate.Contains('\\') ||
            Uri.TryCreate(candidate, UriKind.Absolute, out _))
        {
            return fallback;
        }

        var path = candidate.Split(['?', '#'], 2)[0];
        if (path.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment is "." or ".."))
        {
            return fallback;
        }

        return Uri.IsWellFormedUriString(candidate, UriKind.Relative)
            ? candidate
            : fallback;
    }

    public static string ToBundledClientPath(string? returnUrl)
    {
        var relative = NormalizeRelativeReturnUrl(returnUrl, string.Empty).TrimStart('/');
        return string.IsNullOrEmpty(relative)
            ? "/_client/"
            : $"/_client/{relative}";
    }
}
