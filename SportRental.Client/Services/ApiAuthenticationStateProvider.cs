using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Authorization;

namespace SportRental.Client.Services;

public class ApiAuthenticationStateProvider : AuthenticationStateProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILocalStorageService _localStorage;
    private readonly AuthenticationState _anonymous = new(new ClaimsPrincipal(new ClaimsIdentity()));

    public ApiAuthenticationStateProvider(HttpClient httpClient, ILocalStorageService localStorage)
    {
        _httpClient = httpClient;
        _localStorage = localStorage;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var token = await _localStorage.GetItemAsync<string>("authToken");

        if (string.IsNullOrWhiteSpace(token))
            return _anonymous;

        // Set default authorization header
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var claims = ParseClaimsFromJwt(token).ToList();
        
        // If no claims from JWT (cookie-based auth), try to get from stored user info
        if (!claims.Any())
        {
            var userId = await _localStorage.GetItemAsync<string>("userId");
            var email = await _localStorage.GetItemAsync<string>("userEmail");
            
            if (!string.IsNullOrEmpty(email))
            {
                claims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, userId ?? ""),
                    new Claim(ClaimTypes.Email, email),
                    new Claim(ClaimTypes.Name, email)
                };
            }
            else
            {
                // No valid auth info
                return _anonymous;
            }
        }
        else
        {
            // JWT-based auth - check expiry
            var expiry = claims.FirstOrDefault(c => c.Type == "exp")?.Value;

            if (expiry != null && long.TryParse(expiry, out var exp))
            {
                var expiryDate = DateTimeOffset.FromUnixTimeSeconds(exp);
                if (expiryDate < DateTimeOffset.UtcNow)
                {
                    // Token expired. Server does not currently expose a refresh
                    // endpoint, so we simply log the user out and they must
                    // re-authenticate. (Historical refresh flow was dead code,
                    // removed as part of SEC-010 cleanup.)
                    await MarkUserAsLoggedOut();
                    return _anonymous;
                }
            }
        }

        var identity = new ClaimsIdentity(claims, "jwt");
        var user = new ClaimsPrincipal(identity);

        return new AuthenticationState(user);
    }

    public async Task MarkUserAsAuthenticated(string token, string? userId = null, string? email = null)
    {
        await _localStorage.SetItemAsync("authToken", token);

        // Store user info for cookie-based auth
        if (!string.IsNullOrEmpty(userId))
            await _localStorage.SetItemAsync("userId", userId);
        if (!string.IsNullOrEmpty(email))
            await _localStorage.SetItemAsync("userEmail", email);

        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var claims = ParseClaimsFromJwt(token);
        
        // If no claims from JWT (cookie-based auth), create claims from stored user info
        if (!claims.Any() && !string.IsNullOrEmpty(email))
        {
            claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, userId ?? ""),
                new Claim(ClaimTypes.Email, email),
                new Claim(ClaimTypes.Name, email)
            };
        }
        
        var identity = new ClaimsIdentity(claims, "jwt");
        var user = new ClaimsPrincipal(identity);

        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(user)));
    }

    public async Task MarkUserAsLoggedOut()
    {
        await _localStorage.RemoveItemAsync("authToken");
        // Legacy key from the removed refresh-token flow — clear to avoid stale data.
        await _localStorage.RemoveItemAsync("refreshToken");
        await _localStorage.RemoveItemAsync("userId");
        await _localStorage.RemoveItemAsync("userEmail");

        _httpClient.DefaultRequestHeaders.Authorization = null;

        NotifyAuthenticationStateChanged(Task.FromResult(_anonymous));
    }

    private static IEnumerable<Claim> ParseClaimsFromJwt(string jwt)
    {
        // Handle cookie-based auth or invalid JWT
        var parts = jwt.Split('.');
        if (parts.Length != 3)
        {
            // Not a valid JWT - return empty claims
            return Enumerable.Empty<Claim>();
        }

        var payload = parts[1];
        var jsonBytes = ParseBase64WithoutPadding(payload);
        var keyValuePairs = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonBytes);

        if (keyValuePairs == null)
            return Enumerable.Empty<Claim>();

        var claims = new List<Claim>();

        foreach (var kvp in keyValuePairs)
        {
            if (kvp.Value is JsonElement element)
            {
                if (element.ValueKind == JsonValueKind.String)
                {
                    claims.Add(new Claim(kvp.Key, element.GetString() ?? string.Empty));
                }
                else if (element.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in element.EnumerateArray())
                    {
                        claims.Add(new Claim(kvp.Key, item.GetString() ?? string.Empty));
                    }
                }
                else
                {
                    claims.Add(new Claim(kvp.Key, element.ToString()));
                }
            }
        }

        return claims;
    }

    private static byte[] ParseBase64WithoutPadding(string base64)
    {
        switch (base64.Length % 4)
        {
            case 2: base64 += "=="; break;
            case 3: base64 += "="; break;
        }
        return Convert.FromBase64String(base64);
    }

}
