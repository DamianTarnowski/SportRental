using Microsoft.Extensions.Options;
using SMSApi.Api;
using System.Text.RegularExpressions;

namespace SportRental.Admin.Services.Sms
{
    /// <summary>
    /// Implementacja ISmsSender wykorzystująca SMSAPI.pl
    /// </summary>
    public class SmsApiSender : ISmsSender
    {
        private readonly SmsApiSettings _settings;
        private readonly ILogger<SmsApiSender> _logger;
        private readonly IClient? _client;

        public SmsApiSender(IOptions<SmsApiSettings> settings, ILogger<SmsApiSender> logger)
        {
            _settings = settings.Value;
            _logger = logger;

            if (_settings.IsEnabled && !string.IsNullOrWhiteSpace(_settings.AuthToken))
            {
                _client = new ClientOAuth(_settings.AuthToken);
            }
        }

        public async Task SendAsync(string phoneNumber, string message, CancellationToken ct = default)
        {
            var normalizedPhone = NormalizePhoneNumber(phoneNumber);
            var sanitizedMessage = SanitizeSmsText(message);

            if (!_settings.IsEnabled || _client == null)
            {
                Console.WriteLine($"[SMS-DISABLED] {normalizedPhone}: {sanitizedMessage}");
                _logger.LogInformation("[SMS-DISABLED] To: {PhoneNumber}, Message: {Message}", normalizedPhone, sanitizedMessage);
                return;
            }

            var attempts = 0;
            var maxAttempts = _settings.SendConfirmationAttempts;
            SMSApi.Api.Exception? lastException = null;

            while (attempts < maxAttempts)
            {
                attempts++;
                try
                {
                    await SendSmsInternalAsync(normalizedPhone, sanitizedMessage);
                    _logger.LogInformation("SMS sent successfully to {PhoneNumber} on attempt {Attempt}", normalizedPhone, attempts);
                    return;
                }
                catch (SMSApi.Api.Exception ex)
                {
                    lastException = ex;
                    _logger.LogWarning(ex, "Failed to send SMS to {PhoneNumber} on attempt {Attempt}/{MaxAttempts}", 
                        normalizedPhone, attempts, maxAttempts);

                    if (attempts < maxAttempts)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(2), ct);
                    }
                }
            }

            _logger.LogError(lastException, "Failed to send SMS to {PhoneNumber} after {MaxAttempts} attempts", 
                normalizedPhone, maxAttempts);
            throw new InvalidOperationException($"Failed to send SMS after {maxAttempts} attempts", lastException);
        }

        private static string SanitizeSmsText(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return message;

            var sanitized = message;

            // Remove URLs
            sanitized = Regex.Replace(sanitized, @"\bhttps?://\S+\b", "", RegexOptions.IgnoreCase);
            sanitized = Regex.Replace(sanitized, @"\bwww\.\S+\b", "", RegexOptions.IgnoreCase);

            // Remove emails
            sanitized = Regex.Replace(sanitized, @"\b[\w.\-+%]+@[\w.\-]+\.[A-Za-z]{2,}\b", "", RegexOptions.IgnoreCase);

            // Some providers also flag domain-like patterns; remove bare domains like example.com
            sanitized = Regex.Replace(sanitized, @"\b[\w\-]+\.(pl|com|net|org|io|dev|eu|info)\b", "", RegexOptions.IgnoreCase);

            // Normalize whitespace
            sanitized = Regex.Replace(sanitized, @"\s{2,}", " ").Trim();

            return sanitized;
        }

        private async Task SendSmsInternalAsync(string phoneNumber, string message)
        {
            var smsFactory = new SMSFactory(_client);
            var response = await smsFactory.ActionSend()
                .SetText(message)
                .SetTo(phoneNumber)
                .SetSender(_settings.SenderName)
                .ExecuteAsync();

            _logger.LogDebug("SMSAPI response: {Count} messages sent", response.Count);
        }

        /// <summary>
        /// Normalizuje numer telefonu - usuwa +48 i spacje
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

            // Usuń prefix +48 lub 48 na początku
            if (cleaned.StartsWith("+48"))
                cleaned = cleaned[3..];
            else if (cleaned.StartsWith("48") && cleaned.Length > 9)
                cleaned = cleaned[2..];

            return cleaned;
        }

        public Task SendThanksMessageAsync(string phoneNumber, string customerName, string? customMessage = null, CancellationToken ct = default)
        {
            var message = string.IsNullOrWhiteSpace(customMessage)
                ? $"Dziękujemy {customerName} za wypożyczenie sprzętu w SportRental!"
                : customMessage;
            return SendAsync(phoneNumber, message, ct);
        }

        public Task SendReminderAsync(string phoneNumber, string customerName, string? customMessage = null, CancellationToken ct = default)
        {
            var message = string.IsNullOrWhiteSpace(customMessage)
                ? $"Przypominamy {customerName} o zbliżającym się terminie zwrotu sprzętu - SportRental"
                : customMessage;
            return SendAsync(phoneNumber, message, ct);
        }

        public Task SendConfirmationRequestAsync(string phoneNumber, string customerName, Guid rentalId, CancellationToken ct = default)
        {
            var message = $"Witaj {customerName}! Potwierdzenie wynajmu {rentalId.ToString()[..8]}. Nie odpowiadaj na tę wiadomość - SportRental";
            return SendAsync(phoneNumber, message, ct);
        }
        
        public Task SendContractConfirmationRequestAsync(string phoneNumber, string customerName, Guid rentalId, CancellationToken ct = default)
        {
            return SendContractConfirmationRequestAsync(phoneNumber, customerName, rentalId, null, ct);
        }
        
        public Task SendContractConfirmationRequestAsync(string phoneNumber, string customerName, Guid rentalId, string? customerEmail, CancellationToken ct = default)
        {
            var rentalCode = rentalId.ToString()[..8].ToUpper();
            var message = $"SportRental: {customerName}, czy potwierdzasz umowe nr {rentalCode}? Odpisz TAK lub NIE.";
            return SendAsync(phoneNumber, message, ct);
        }
    }
}
