namespace SportRental.Client.Services;

public static class ApiBaseUrlResolver
{
    private const string DefaultDevelopmentApiUrl = "http://localhost:5001";

    public static string Resolve(string hostAddress, string? configuredBaseUrl)
    {
        var hostUri = new Uri(hostAddress, UriKind.Absolute);
        var hostPath = hostUri.AbsolutePath.TrimEnd('/');

        // Admin bundles the WASM client under /_client on every domain and port.
        // Domain-name checks break custom domains, reverse proxies and local test hosts.
        if (hostPath.EndsWith("/_client", StringComparison.OrdinalIgnoreCase))
        {
            return hostUri.GetLeftPart(UriPartial.Authority);
        }

        return string.IsNullOrWhiteSpace(configuredBaseUrl)
            ? DefaultDevelopmentApiUrl
            : configuredBaseUrl.TrimEnd('/');
    }
}
