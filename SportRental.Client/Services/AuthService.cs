using System.Net.Http.Json;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Configuration;
using SportRental.Shared.Legal;

namespace SportRental.Client.Services;

public class AuthService
{
    private readonly HttpClient _httpClient;
    private readonly AuthenticationStateProvider _authStateProvider;
    private readonly ICustomerSessionService _customerSession;
    private readonly string _apiBaseUrl;

    public AuthService(
        HttpClient httpClient,
        AuthenticationStateProvider authStateProvider,
        ICustomerSessionService customerSession,
        IConfiguration configuration)
    {
        _httpClient = httpClient;
        _authStateProvider = authStateProvider;
        _customerSession = customerSession;
        _apiBaseUrl = (configuration["Api:BaseUrl"] ?? "http://localhost:5001").TrimEnd('/');
    }

    public async Task<AuthResult> RegisterAsync(string email, string password, string? fullName = null, string? phoneNumber = null, string? documentNumber = null)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_apiBaseUrl}/api/auth/register")
            {
                Content = JsonContent.Create(new
                {
                    Email = email,
                    Password = password,
                    FullName = fullName,
                    PhoneNumber = phoneNumber,
                    DocumentNumber = documentNumber,
                    AcceptedTermsVersion = LegalDocumentVersions.Terms,
                    AcknowledgedPrivacyVersion = LegalDocumentVersions.Privacy
                })
            };

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
                return AuthResult.Failure(error?.Error ?? "Rejestracja nie powiodła się");
            }

            var result = await response.Content.ReadFromJsonAsync<AuthResponse>();
            if (result == null)
                return AuthResult.Failure("Nieprawidłowa odpowiedź serwera");

            if (result.EmailConfirmationRequired)
                return AuthResult.ConfirmationRequired();

            await ((ApiAuthenticationStateProvider)_authStateProvider).MarkUserAsAuthenticated(
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
                        var error = System.Text.Json.JsonSerializer.Deserialize<ErrorResponse>(
                            content,
                            System.Text.Json.JsonSerializerOptions.Web);
                        return AuthResult.Failure(
                            error?.Error ?? "Logowanie nie powiodło się",
                            error?.Code);
                    }
                    catch { }
                }
                return AuthResult.Failure("Logowanie nie powiodło się");
            }

            var result = await response.Content.ReadFromJsonAsync<AuthResponse>();
            if (result == null)
                return AuthResult.Failure("Nieprawidłowa odpowiedź serwera");

            await ((ApiAuthenticationStateProvider)_authStateProvider).MarkUserAsAuthenticated(
                result.User?.Id.ToString(),
                result.User?.Email);

            return AuthResult.Success();
        }
        catch (Exception ex)
        {
            return AuthResult.Failure($"Błąd: {ex.Message}");
        }
    }

    public async Task<bool> IsGoogleLoginAvailableAsync()
    {
        try
        {
            var providers = await _httpClient.GetFromJsonAsync<AuthProvidersResponse>(
                $"{_apiBaseUrl}/api/auth/providers");
            return providers?.Google == true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<AuthResult> ResendEmailConfirmationAsync(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return AuthResult.Failure("Podaj adres e-mail.");

        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                $"{_apiBaseUrl}/api/auth/resend-confirmation",
                new { Email = email });
            if (!response.IsSuccessStatusCode)
                return AuthResult.Failure("Nie udało się ponowić wysyłki. Spróbuj ponownie później.");

            return AuthResult.Success();
        }
        catch
        {
            return AuthResult.Failure("Nie udało się połączyć z serwerem. Spróbuj ponownie później.");
        }
    }

    public async Task LogoutAsync()
    {
        try
        {
            await ((ApiAuthenticationStateProvider)_authStateProvider).MarkUserAsLoggedOut();
        }
        finally
        {
            await _customerSession.ClearAsync();
        }
    }

    // SEC-009: po /auth/login, /auth/register, /auth/guest-session serwer ustawia HttpOnly cookie
    // i NIE zwraca już JWT w body — stąd brak AccessToken/TokenType w response.
    private record AuthResponse(int ExpiresIn, UserInfo? User, bool EmailConfirmationRequired = false);
    private record UserInfo(Guid Id, string Email, Guid? TenantId, Guid? CustomerId);
    private record ErrorResponse(string Error, string? Code = null);
    private record AuthProvidersResponse(bool Google);
}

public class AuthResult
{
    public bool Succeeded { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ErrorCode { get; init; }
    public bool EmailConfirmationRequired { get; init; }

    public static AuthResult Success() => new() { Succeeded = true };
    public static AuthResult ConfirmationRequired() => new()
    {
        Succeeded = true,
        EmailConfirmationRequired = true
    };
    public static AuthResult Failure(string error, string? code = null) => new()
    {
        Succeeded = false,
        ErrorMessage = error,
        ErrorCode = code
    };
}
