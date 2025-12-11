namespace SportRental.Admin.Services.Sms
{
    public class ConsoleSmsSender : ISmsSender
    {
        public Task SendAsync(string phoneNumber, string message, CancellationToken ct = default)
        {
            Console.WriteLine($"[SMS] {phoneNumber}: {message}");
            return Task.CompletedTask;
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
            var contractUrl = $"https://sradmin2.azurewebsites.net/c/{rentalId.ToString()[..8].ToLower()}";
            var emailInfo = !string.IsNullOrWhiteSpace(customerEmail) ? $" wysłanej na {customerEmail}" : "";
            var message = $"SportRental: {customerName}, czy potwierdzasz warunki umowy{emailInfo}? {contractUrl} Odpisz TAK lub NIE.";
            return SendAsync(phoneNumber, message, ct);
        }
    }
}




