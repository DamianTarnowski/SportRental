using System.Net.Http.Json;
using System.Security.Claims;
using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Configuration;

namespace SportRental.Client.Services;

// SEC-009: po migracji tokena do HttpOnly cookie (`sr_access_token`) ten provider
// nie zna już samego JWT — stan uwierzytelnienia pytamy serwera przez GET /api/auth/me.
// Nie utrwalamy tożsamości ani PII w localStorage; źródłem prawdy jest wyłącznie
// chronione HttpOnly cookie i odpowiedź serwera.
public class ApiAuthenticationStateProvider : AuthenticationStateProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILocalStorageService _localStorage;
    private readonly IConfiguration _configuration;
    private readonly AuthenticationState _anonymous = new(new ClaimsPrincipal(new ClaimsIdentity()));
    private readonly object _stateLock = new();
    private Task<AuthenticationState>? _cachedAuthenticationState;
    private AuthenticationState _lastKnownState;
    private DateTimeOffset _cacheExpiresAtUtc;
    private static readonly TimeSpan StateCacheDuration = TimeSpan.FromSeconds(30);

    public ApiAuthenticationStateProvider(HttpClient httpClient, ILocalStorageService localStorage, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _localStorage = localStorage;
        _configuration = configuration;
        _lastKnownState = _anonymous;
    }

    private string ApiBaseUrl => (_configuration["Api:BaseUrl"] ?? "http://localhost:5001").TrimEnd('/');

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        lock (_stateLock)
        {
            if (_cachedAuthenticationState is not null && DateTimeOffset.UtcNow < _cacheExpiresAtUtc)
            {
                return _cachedAuthenticationState;
            }

            _cachedAuthenticationState = LoadAuthenticationStateAsync();
            _cacheExpiresAtUtc = DateTimeOffset.UtcNow.Add(StateCacheDuration);
            return _cachedAuthenticationState;
        }
    }

    private async Task<AuthenticationState> LoadAuthenticationStateAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{ApiBaseUrl}/api/auth/me");
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                response.Dispose();
                using var refreshResponse = await _httpClient.PostAsync(
                    $"{ApiBaseUrl}/api/auth/refresh",
                    content: null);
                if (refreshResponse.IsSuccessStatusCode)
                    response = await _httpClient.GetAsync($"{ApiBaseUrl}/api/auth/me");
                else if (refreshResponse.StatusCode is System.Net.HttpStatusCode.Unauthorized
                         or System.Net.HttpStatusCode.Forbidden)
                {
                    await ClearLocalUserInfoAsync();
                    SetLastKnownState(_anonymous);
                    return _anonymous;
                }
                else
                    return GetLastKnownState();
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized
                        or System.Net.HttpStatusCode.Forbidden)
                    {
                        await ClearLocalUserInfoAsync();
                        SetLastKnownState(_anonymous);
                        return _anonymous;
                    }

                    return GetLastKnownState();
                }

                var me = await response.Content.ReadFromJsonAsync<MeResponse>();
                if (me is null || string.IsNullOrEmpty(me.Email))
                {
                    return GetLastKnownState();
                }

                await ClearLocalUserInfoAsync();

                var claims = new List<Claim>
                {
                    new(ClaimTypes.NameIdentifier, me.Id ?? ""),
                    new(ClaimTypes.Email, me.Email),
                    new(ClaimTypes.Name, me.Email)
                };
                foreach (var role in me.Roles ?? Array.Empty<string>())
                {
                    claims.Add(new Claim(ClaimTypes.Role, role));
                }
                if (me.TenantId is { } tenantId && tenantId != Guid.Empty)
                {
                    claims.Add(new Claim("tenant-id", tenantId.ToString()));
                }
                if (me.CustomerId is { } customerId && customerId != Guid.Empty)
                {
                    claims.Add(new Claim("customer-id", customerId.ToString()));
                }

                var identity = new ClaimsIdentity(claims, "cookie-jwt");
                var authenticated = new AuthenticationState(new ClaimsPrincipal(identity));
                SetLastKnownState(authenticated);
                return authenticated;
            }
        }
        catch
        {
            // Błąd sieci/5xx nie jest dowodem wylogowania. Zachowujemy ostatni
            // potwierdzony stan wyłącznie w pamięci bieżącej karty; 401/403 nadal
            // jednoznacznie czyszczą sesję powyżej.
            return GetLastKnownState();
        }
    }

    // Wywoływane po udanym loginie lub guest-session — serwer już ustawił cookie.
    // Rejestracja oczekująca na potwierdzenie adresu nie wywołuje tej metody.
    // Czyścimy legacy dane lokalne i notyfikujemy o zmianie stanu.
    public async Task MarkUserAsAuthenticated(string? userId = null, string? email = null)
    {
        await ClearLocalUserInfoAsync();

        InvalidateCachedState();
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    public async Task MarkUserAsLoggedOut()
    {
        // Wywołujemy logout na serwerze, żeby skasować HttpOnly cookie.
        try
        {
            await _httpClient.PostAsync($"{ApiBaseUrl}/api/auth/logout", content: null);
        }
        catch
        {
            // best-effort — jeśli serwer nieosiągalny, dalej czyścimy lokalny stan.
        }

        await ClearLocalUserInfoAsync();
        var anonymousTask = Task.FromResult(_anonymous);
        lock (_stateLock)
        {
            _cachedAuthenticationState = anonymousTask;
            _cacheExpiresAtUtc = DateTimeOffset.UtcNow.Add(StateCacheDuration);
            _lastKnownState = _anonymous;
        }
        NotifyAuthenticationStateChanged(anonymousTask);
    }

    private void InvalidateCachedState()
    {
        lock (_stateLock)
        {
            _cachedAuthenticationState = null;
            _cacheExpiresAtUtc = default;
        }
    }

    private AuthenticationState GetLastKnownState()
    {
        lock (_stateLock)
        {
            return _lastKnownState;
        }
    }

    private void SetLastKnownState(AuthenticationState state)
    {
        lock (_stateLock)
        {
            _lastKnownState = state;
        }
    }

    private async Task ClearLocalUserInfoAsync()
    {
        await _localStorage.RemoveItemAsync("userId");
        await _localStorage.RemoveItemAsync("userEmail");
        // Legacy keys z poprzednich wersji — czyścimy, żeby nie zostały wiszące wrażliwe dane.
        await _localStorage.RemoveItemAsync("authToken");
        await _localStorage.RemoveItemAsync("refreshToken");
    }

    private sealed record MeResponse(string? Id, string? Email, Guid? TenantId, Guid? CustomerId, string[]? Roles);
}
