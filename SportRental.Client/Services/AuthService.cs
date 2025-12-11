using System.Net.Http.Json;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Configuration;

namespace SportRental.Client.Services;

public class AuthService
{
    private readonly HttpClient _httpClient;
    private readonly AuthenticationStateProvider _authStateProvider;
    private readonly TenantService _tenantService;
    private readonly string _apiBaseUrl;
    private readonly string _defaultTenantId;

    public AuthService(HttpClient httpClient, AuthenticationStateProvider authStateProvider, TenantService tenantService, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _authStateProvider = authStateProvider;
        _tenantService = tenantService;
        _apiBaseUrl = configuration["Api:BaseUrl"] ?? "http://localhost:5002";
        _defaultTenantId = configuration["Api:TenantId"] ?? "547f5df7-a389-44b3-bcc6-090ff2fa92e5";
    }

    public async Task<AuthResult> RegisterAsync(string email, string password, string? fullName = null, string? phoneNumber = null, string? documentNumber = null)
    {
        try
        {
            // Użyj wybranego tenanta lub domyślnego z konfiguracji
            var tenantId = await _tenantService.GetSelectedTenantIdAsync();
            if (string.IsNullOrEmpty(tenantId))
            {
                tenantId = _defaultTenantId;
            }

            var request = new HttpRequestMessage(HttpMethod.Post, $"{_apiBaseUrl}/api/auth/register")
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

            await ((ApiAuthenticationStateProvider)_authStateProvider).MarkUserAsAuthenticated(
                result.AccessToken, 
                result.RefreshToken,
                result.User?.Id.ToString(),
                result.User?.Email);

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
            // Login nie wymaga tenant ID - użytkownik ma przypisany tenant w bazie
            var response = await _httpClient.PostAsJsonAsync($"{_apiBaseUrl}/api/auth/login", new
            {
                Email = email,
                Password = password
            });

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    return AuthResult.Failure("Nieprawidłowy email lub hasło");
                }
                
                var content = await response.Content.ReadAsStringAsync();
                if (!string.IsNullOrWhiteSpace(content))
                {
                    try
                    {
                        var error = System.Text.Json.JsonSerializer.Deserialize<ErrorResponse>(content);
                        return AuthResult.Failure(error?.Error ?? "Logowanie nie powiodło się");
                    }
                    catch { }
                }
                return AuthResult.Failure("Logowanie nie powiodło się");
            }

            var result = await response.Content.ReadFromJsonAsync<AuthResponse>();
            if (result == null)
                return AuthResult.Failure("Nieprawidłowa odpowiedź serwera");

            await ((ApiAuthenticationStateProvider)_authStateProvider).MarkUserAsAuthenticated(
                result.AccessToken, 
                result.RefreshToken,
                result.User?.Id.ToString(),
                result.User?.Email);

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
