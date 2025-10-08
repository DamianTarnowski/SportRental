using Microsoft.Extensions.Configuration;
using System.Net.Http.Json;

namespace SportRental.Admin.Services.Sms
{
    public class SmsApiSender : ISmsSender
    {
        private readonly HttpClient _http;
        private readonly string _token;
        private readonly string? _from;

        public SmsApiSender(IConfiguration config, IHttpClientFactory httpClientFactory)
        {
            _http = httpClientFactory.CreateClient("smsapi");
            _http.BaseAddress = new Uri("https://api.smsapi.pl/");
            _token = config["SmsApi:Token"] ?? string.Empty;
            _from = config["SmsApi:From"];
        }

        public async Task SendAsync(string phoneNumber, string message, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(_token))
            {
                // Fallback do konsoli jeśli brak tokenu
                Console.WriteLine($"[SMSAPI-MOCK] {phoneNumber}: {message}");
                return;
            }

            using var req = new HttpRequestMessage(HttpMethod.Post, "sms.do");
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _token);
            var data = new Dictionary<string, string>
            {
                ["to"] = phoneNumber,
                ["message"] = message
            };
            if (!string.IsNullOrWhiteSpace(_from))
                data["from"] = _from!;

            req.Content = new FormUrlEncodedContent(data);
            var res = await _http.SendAsync(req, ct);
            res.EnsureSuccessStatusCode();
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
    }
}




