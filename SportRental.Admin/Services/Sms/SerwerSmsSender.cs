using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SportRental.Admin.Services.Sms;

/// <summary>
/// Ustawienia dla SerwerSMS.pl
/// </summary>
public class SerwerSmsSettings
{
    public const string SectionName = "SerwerSms";
    
    /// <summary>
    /// Czy wysyłanie SMS jest włączone
    /// </summary>
    public bool IsEnabled { get; set; }
    
    /// <summary>
    /// Login użytkownika API (utworzony w Panelu Klienta → Ustawienia interfejsów → HTTP API → Użytkownicy API)
    /// </summary>
    public string Username { get; set; } = string.Empty;
    
    /// <summary>
    /// Hasło użytkownika API
    /// </summary>
    public string Password { get; set; } = string.Empty;
    
    /// <summary>
    /// Nazwa nadawcy dla SMS FULL (np. "SportRental"). Jeśli puste - wysyła SMS ECO.
    /// </summary>
    public string? SenderName { get; set; }
    
    /// <summary>
    /// Czy wysyłać SMS FULL (z własnym nadawcą) czy ECO (tańsze, bez nadawcy)
    /// </summary>
    public bool UseSmsEco { get; set; } = true;
    
    /// <summary>
    /// Liczba prób wysłania SMS
    /// </summary>
    public int MaxRetries { get; set; } = 3;
    
    /// <summary>
    /// Tryb testowy - SMS nie jest wysyłany, tylko symulowany
    /// </summary>
    public bool TestMode { get; set; } = false;
}

/// <summary>
/// Implementacja ISmsSender wykorzystująca SerwerSMS.pl
/// </summary>
public class SerwerSmsSender : ISmsSender
{
    private const string ApiBaseUrl = "https://api2.serwersms.pl/";
    
    private readonly SerwerSmsSettings _settings;
    private readonly ILogger<SerwerSmsSender> _logger;
    private readonly HttpClient _httpClient;

    public SerwerSmsSender(IOptions<SerwerSmsSettings> settings, ILogger<SerwerSmsSender> logger, IHttpClientFactory httpClientFactory)
    {
        _settings = settings.Value;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient("SerwerSms");
        _httpClient.BaseAddress = new Uri(ApiBaseUrl);
    }

    public async Task SendAsync(string phoneNumber, string message, CancellationToken ct = default)
    {
        var normalizedPhone = NormalizePhoneNumber(phoneNumber);

        if (!_settings.IsEnabled)
        {
            Console.WriteLine($"[SMS-DISABLED] {normalizedPhone}: {message}");
            _logger.LogInformation("[SMS-DISABLED] To: {PhoneNumber}, Message: {Message}", normalizedPhone, message);
            return;
        }

        var attempts = 0;
        Exception? lastException = null;

        while (attempts < _settings.MaxRetries)
        {
            attempts++;
            try
            {
                await SendSmsInternalAsync(normalizedPhone, message, ct);
                _logger.LogInformation("SMS sent successfully to {PhoneNumber} on attempt {Attempt}", normalizedPhone, attempts);
                return;
            }
            catch (Exception ex)
            {
                lastException = ex;
                _logger.LogWarning(ex, "Failed to send SMS to {PhoneNumber} on attempt {Attempt}/{MaxAttempts}", 
                    normalizedPhone, attempts, _settings.MaxRetries);

                if (attempts < _settings.MaxRetries)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), ct);
                }
            }
        }

        _logger.LogError(lastException, "Failed to send SMS to {PhoneNumber} after {MaxAttempts} attempts", 
            normalizedPhone, _settings.MaxRetries);
        throw new InvalidOperationException($"Failed to send SMS after {_settings.MaxRetries} attempts", lastException);
    }

    private async Task SendSmsInternalAsync(string phoneNumber, string message, CancellationToken ct)
    {
        var request = new Dictionary<string, string>
        {
            ["username"] = _settings.Username,
            ["password"] = _settings.Password,
            ["phone"] = phoneNumber,
            ["text"] = message,
            ["details"] = "1"
        };

        // Jeśli nie używamy ECO i mamy nazwę nadawcy - wysyłamy SMS FULL
        if (!_settings.UseSmsEco && !string.IsNullOrWhiteSpace(_settings.SenderName))
        {
            request["sender"] = _settings.SenderName;
        }

        // Tryb testowy
        if (_settings.TestMode)
        {
            request["test"] = "1";
        }

        var content = new FormUrlEncodedContent(request);
        var response = await _httpClient.PostAsync("messages/send_sms.json", content, ct);
        
        var responseBody = await response.Content.ReadAsStringAsync(ct);
        _logger.LogDebug("SerwerSMS response: {Response}", responseBody);

        var result = JsonSerializer.Deserialize<SerwerSmsResponse>(responseBody);

        if (result == null)
        {
            throw new InvalidOperationException("Invalid response from SerwerSMS API");
        }

        if (!result.Success)
        {
            var errorMessage = result.Error?.Message ?? "Unknown error";
            var errorCode = result.Error?.Code ?? 0;
            throw new InvalidOperationException($"SerwerSMS error [{errorCode}]: {errorMessage}");
        }

        _logger.LogInformation("SMS queued: {Queued}, unsent: {Unsent}", result.Queued, result.Unsent);
    }

    /// <summary>
    /// Normalizuje numer telefonu do formatu międzynarodowego +48XXXXXXXXX
    /// </summary>
    private static string NormalizePhoneNumber(string phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
            return phoneNumber;

        // Usuń spacje, myślniki i nawiasy
        var cleaned = phoneNumber
            .Replace(" ", "")
            .Replace("-", "")
            .Replace("(", "")
            .Replace(")", "");

        // Jeśli zaczyna się od +, zostaw jak jest
        if (cleaned.StartsWith("+"))
            return cleaned;

        // Jeśli zaczyna się od 48, dodaj +
        if (cleaned.StartsWith("48") && cleaned.Length > 9)
            return "+" + cleaned;

        // Jeśli zaczyna się od 0, usuń 0 i dodaj +48
        if (cleaned.StartsWith("0"))
            return "+48" + cleaned[1..];

        // W przeciwnym razie dodaj +48
        return "+48" + cleaned;
    }

    public Task SendThanksMessageAsync(string phoneNumber, string customerName, string? customMessage = null, CancellationToken ct = default)
    {
        var message = string.IsNullOrWhiteSpace(customMessage)
            ? $"Dziekujemy {customerName} za wypozyczenie sprzetu w SportRental!"
            : customMessage;
        return SendAsync(phoneNumber, message, ct);
    }

    public Task SendReminderAsync(string phoneNumber, string customerName, string? customMessage = null, CancellationToken ct = default)
    {
        var message = string.IsNullOrWhiteSpace(customMessage)
            ? $"Przypominamy {customerName} o zblizajacym sie terminie zwrotu sprzetu - SportRental"
            : customMessage;
        return SendAsync(phoneNumber, message, ct);
    }

    public Task SendConfirmationRequestAsync(string phoneNumber, string customerName, Guid rentalId, CancellationToken ct = default)
    {
        var message = $"Witaj {customerName}! Potwierdzenie wynajmu {rentalId.ToString()[..8]}. Nie odpowiadaj na te wiadomosc - SportRental";
        return SendAsync(phoneNumber, message, ct);
    }
    
    public Task SendContractConfirmationRequestAsync(string phoneNumber, string customerName, Guid rentalId, CancellationToken ct = default)
    {
        return SendContractConfirmationRequestAsync(phoneNumber, customerName, rentalId, null, ct);
    }
    
    public Task SendContractConfirmationRequestAsync(string phoneNumber, string customerName, Guid rentalId, string? customerEmail, CancellationToken ct = default)
    {
        // Krótki link do umowy - klient może kliknąć i zobaczyć szczegóły
        var contractUrl = $"https://sradmin2.azurewebsites.net/c/{rentalId.ToString()[..8].ToLower()}";
        var emailInfo = !string.IsNullOrWhiteSpace(customerEmail) ? $" wyslanej na {customerEmail}" : "";
        var message = $"SportRental: {customerName}, czy potwierdzasz warunki umowy{emailInfo}? {contractUrl} Odpisz TAK lub NIE.";
        return SendAsync(phoneNumber, message, ct);
    }
}

#region Response Models

internal class SerwerSmsResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }
    
    [JsonPropertyName("queued")]
    public int Queued { get; set; }
    
    [JsonPropertyName("unsent")]
    public int Unsent { get; set; }
    
    [JsonPropertyName("items")]
    public List<SerwerSmsItem>? Items { get; set; }
    
    [JsonPropertyName("error")]
    public SerwerSmsError? Error { get; set; }
}

internal class SerwerSmsItem
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }
    
    [JsonPropertyName("phone")]
    public string? Phone { get; set; }
    
    [JsonPropertyName("status")]
    public string? Status { get; set; }
    
    [JsonPropertyName("queued")]
    public string? Queued { get; set; }
    
    [JsonPropertyName("parts")]
    public int Parts { get; set; }
    
    [JsonPropertyName("text")]
    public string? Text { get; set; }
}

internal class SerwerSmsError
{
    [JsonPropertyName("code")]
    public int Code { get; set; }
    
    [JsonPropertyName("type")]
    public string? Type { get; set; }
    
    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

#endregion
