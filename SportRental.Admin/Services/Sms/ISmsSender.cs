namespace SportRental.Admin.Services.Sms
{
    public interface ISmsSender
    {
        Task SendAsync(string phoneNumber, string message, CancellationToken ct = default);
        Task SendThanksMessageAsync(string phoneNumber, string customerName, string? customMessage = null, CancellationToken ct = default);
        Task SendReminderAsync(string phoneNumber, string customerName, string? customMessage = null, CancellationToken ct = default);
        Task SendConfirmationRequestAsync(string phoneNumber, string customerName, Guid rentalId, CancellationToken ct = default);
        
        /// <summary>
        /// Wysyła SMS z prośbą o potwierdzenie warunków umowy
        /// </summary>
        Task SendContractConfirmationRequestAsync(string phoneNumber, string customerName, Guid rentalId, CancellationToken ct = default);
        
        /// <summary>
        /// Wysyła SMS z prośbą o potwierdzenie warunków umowy z informacją o emailu
        /// </summary>
        Task SendContractConfirmationRequestAsync(string phoneNumber, string customerName, Guid rentalId, string? customerEmail, CancellationToken ct = default);
    }

    [Obsolete("Legacy SMS reply confirmation flow. Use IRentalConfirmationService link confirmation instead.")]
    public interface ISmsConfirmationService
    {
        Task<string> GenerateConfirmationCodeAsync(Guid rentalId, CancellationToken ct = default);
        Task<bool> ValidateConfirmationCodeAsync(Guid rentalId, string code, CancellationToken ct = default);
        Task MarkRentalAsConfirmedAsync(Guid rentalId, CancellationToken ct = default);
        
        /// <summary>
        /// Przetwarza przychodzący SMS i sprawdza czy to potwierdzenie umowy
        /// </summary>
        Task<SmsProcessingResult> ProcessIncomingSmsAsync(string phoneNumber, string message, string? messageId = null, CancellationToken ct = default);
    }
    
    /// <summary>
    /// Wynik przetwarzania przychodzącego SMS
    /// </summary>
    public record SmsProcessingResult(
        bool IsProcessed,
        bool IsConfirmation,
        Guid? RentalId,
        string? ResponseMessage);
}




