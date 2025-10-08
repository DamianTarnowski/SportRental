namespace SportRental.Admin.Services.Sms
{
    public interface ISmsSender
    {
        Task SendAsync(string phoneNumber, string message, CancellationToken ct = default);
        Task SendThanksMessageAsync(string phoneNumber, string customerName, string? customMessage = null, CancellationToken ct = default);
        Task SendReminderAsync(string phoneNumber, string customerName, string? customMessage = null, CancellationToken ct = default);
        Task SendConfirmationRequestAsync(string phoneNumber, string customerName, Guid rentalId, CancellationToken ct = default);
    }

    public interface ISmsConfirmationService
    {
        Task<string> GenerateConfirmationCodeAsync(Guid rentalId, CancellationToken ct = default);
        Task<bool> ValidateConfirmationCodeAsync(Guid rentalId, string code, CancellationToken ct = default);
        Task MarkRentalAsConfirmedAsync(Guid rentalId, CancellationToken ct = default);
    }
}




