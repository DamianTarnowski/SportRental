using System.Net.Http.Json;
using System.Security.Claims;
using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Configuration;

namespace SportRental.Client.Services;

// SEC-009: po migracji tokena do HttpOnly cookie (`sr_access_token`) ten provider
// nie zna już samego JWT — stan uwierzytelnienia pytamy serwera przez GET /api/auth/me.
// W localStorage trzymamy wyłącznie identyfikatory (userId, email) dla szybkiego UI
// przed otrzymaniem odpowiedzi z /me. Te dane nie są poufne (email i tak widać po zalogowaniu).
public class ApiAuthenticationStateProvider : AuthenticationStateProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILocalStorageService _localStorage;
    private readonly IConfiguration _configuration;
    private readonly AuthenticationState _anonymous = new(new ClaimsPrincipal(new ClaimsIdentity()));

    public ApiAuthenticationStateProvider(HttpClient httpClient, ILocalStorageService localStorage, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _localStorage = localStorage;
        _configuration = configuration;
    }

    private string ApiBaseUrl => (_configuration["Api:BaseUrl"] ?? "http://localhost:5001").TrimEnd('/');

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{ApiBaseUrl}/api/auth/me");
            if (!response.IsSuccessStatusCode)
            {
                await ClearLocalUserInfoAsync();
                return _anonymous;
            }

            var me = await response.Content.ReadFromJsonAsync<MeResponse>();
            if (me is null || string.IsNullOrEmpty(me.Email))
            {
                await ClearLocalUserInfoAsync();
                return _anonymous;
            }

            await _localStorage.SetItemAsync("userId", me.Id ?? "");
            await _localStorage.SetItemAsync("userEmail", me.Email);

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

            var identity = new ClaimsIdentity(claims, "cookie-jwt");
            return new AuthenticationState(new ClaimsPrincipal(identity));
        }
        catch
        {
            // Sieć padła lub CORS — traktujemy jako niezalogowany, ale nie czyścimy localStorage
            // (żeby nie tracić UI "zapamiętanego" maila przy chwilowej utracie połączenia).
            return _anonymous;
        }
    }

    // Wywoływane po udanym login/register/guest — serwer już ustawił cookie.
    // Tylko zapisujemy identyfikatory UI i notyfikujemy o zmianie stanu.
    public async Task MarkUserAsAuthenticated(string? userId = null, string? email = null)
    {
        if (!string.IsNullOrEmpty(userId))
            await _localStorage.SetItemAsync("userId", userId);
        if (!string.IsNullOrEmpty(email))
            await _localStorage.SetItemAsync("userEmail", email);

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
        NotifyAuthenticationStateChanged(Task.FromResult(_anonymous));
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
