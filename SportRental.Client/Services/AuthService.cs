using System.Net.Http.Json;
using Microsoft.AspNetCore.Components.Authorization;

namespace SportRental.Client.Services;

public class AuthService
{
    private readonly HttpClient _httpClient;
    private readonly AuthenticationStateProvider _authStateProvider;
    private readonly TenantService _tenantService;

    public AuthService(HttpClient httpClient, AuthenticationStateProvider authStateProvider, TenantService tenantService)
    {
        _httpClient = httpClient;
        _authStateProvider = authStateProvider;
        _tenantService = tenantService;
    }

    public async Task<AuthResult> RegisterAsync(string email, string password, string? fullName = null, string? phoneNumber = null, string? documentNumber = null)
    {
        try
        {
            // Get tenant ID
            var tenantId = await _tenantService.GetSelectedTenantIdAsync();
            if (string.IsNullOrEmpty(tenantId))
            {
                return AuthResult.Failure("Wybierz wypożyczalnię przed rejestracją");
            }

            var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/register")
            {
                Content = JsonContent.Create(new
                {
                    Email = email,
                    Password = password,
                    FullName = fullName,
                    PhoneNumber = phoneNumber,
                    DocumentNumber = documentNumber
                })
            };

            // Add X-Tenant-Id header
            request.Headers.Add("X-Tenant-Id", tenantId);

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
                return AuthResult.Failure(error?.Error ?? "Rejestracja nie powiodła się");
            }

            var result = await response.Content.ReadFromJsonAsync<AuthResponse>();
            if (result == null)
                return AuthResult.Failure("Nieprawidłowa odpowiedź serwera");

            await ((ApiAuthenticationStateProvider)_authStateProvider).MarkUserAsAuthenticated(result.AccessToken, result.RefreshToken);

            return AuthResult.Success();
        }
        catch (Exception ex)
        {
            return AuthResult.Failure($"Błąd: {ex.Message}");
        }
    }

    public async Task<AuthResult> LoginAsync(string email, string password)
    {
        try
        {
            // Get tenant ID for header
            var tenantId = await _tenantService.GetSelectedTenantIdAsync();
            if (string.IsNullOrEmpty(tenantId))
            {
                return AuthResult.Failure("Wybierz wypożyczalnię przed logowaniem");
            }

            var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
            {
                Content = JsonContent.Create(new
                {
                    Email = email,
                    Password = password
                })
            };

            // Add X-Tenant-Id header
            request.Headers.Add("X-Tenant-Id", tenantId);

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
                return AuthResult.Failure(error?.Error ?? "Logowanie nie powiodło się");
            }

            var result = await response.Content.ReadFromJsonAsync<AuthResponse>();
            if (result == null)
                return AuthResult.Failure("Nieprawidłowa odpowiedź serwera");

            await ((ApiAuthenticationStateProvider)_authStateProvider).MarkUserAsAuthenticated(result.AccessToken, result.RefreshToken);

            return AuthResult.Success();
        }
        catch (Exception ex)
        {
            return AuthResult.Failure($"Błąd: {ex.Message}");
        }
    }

    public async Task LogoutAsync()
    {
        await ((ApiAuthenticationStateProvider)_authStateProvider).MarkUserAsLoggedOut();
    }

    private record AuthResponse(string AccessToken, string RefreshToken, int ExpiresIn, string TokenType, UserInfo User);
    private record UserInfo(Guid Id, string Email, Guid TenantId);
    private record ErrorResponse(string Error);
}

public class AuthResult
{
    public bool Succeeded { get; init; }
    public string? ErrorMessage { get; init; }

    public static AuthResult Success() => new() { Succeeded = true };
    public static AuthResult Failure(string error) => new() { Succeeded = false, ErrorMessage = error };
}
