using Microsoft.Extensions.Logging;

namespace SportRental.Admin.Services.Email
{
    public class NoOpEmailSender : IEmailSender
    {
        private readonly ILogger<NoOpEmailSender> _logger;

        public NoOpEmailSender(ILogger<NoOpEmailSender> logger)
        {
            _logger = logger;
        }

        public Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            Validate(email, subject, htmlMessage);
            _logger.LogInformation("[NoOpEmailSender] SendEmailAsync to {Email} with subject '{Subject}' (suppressed in tests)", email, subject);
            return Task.CompletedTask;
        }

        public Task SendEmailWithAttachmentAsync(string email, string subject, string htmlMessage, string? attachmentPath = null)
        {
            Validate(email, subject, htmlMessage);
            _logger.LogInformation("[NoOpEmailSender] SendEmailWithAttachmentAsync to {Email} (suppressed in tests). Attachment: {Attachment}", email, attachmentPath);
            return Task.CompletedTask;
        }

        public Task SendRentalContractAsync(string email, string customerName, byte[] contractPdf)
        {
            if (string.IsNullOrWhiteSpace(customerName)) throw new ArgumentException("Customer name cannot be null or empty.", nameof(customerName));
            if (contractPdf == null || contractPdf.Length == 0) throw new ArgumentException("Contract PDF cannot be null or empty.", nameof(contractPdf));
            Validate(email, "Rental Contract", "PDF attached");
            _logger.LogInformation("[NoOpEmailSender] SendRentalContractAsync to {Email} for {Customer} (suppressed in tests)", email, customerName);
            return Task.CompletedTask;
        }

        public Task SendReminderAsync(string email, string customerName, string reminderText)
        {
            if (string.IsNullOrWhiteSpace(customerName)) throw new ArgumentException("Customer name cannot be null or empty.", nameof(customerName));
            if (string.IsNullOrWhiteSpace(reminderText)) throw new ArgumentException("Reminder text cannot be null or empty.", nameof(reminderText));
            Validate(email, "Reminder", reminderText);
            _logger.LogInformation("[NoOpEmailSender] SendReminderAsync to {Email} for {Customer} (suppressed in tests)", email, customerName);
            return Task.CompletedTask;
        }

        private static void Validate(string email, string subject, string htmlMessage)
        {
            if (string.IsNullOrWhiteSpace(email)) throw new ArgumentException("Email address cannot be null or empty.", nameof(email));
            if (string.IsNullOrWhiteSpace(subject)) throw new ArgumentException("Subject cannot be null or empty.", nameof(subject));
            if (string.IsNullOrWhiteSpace(htmlMessage)) throw new ArgumentException("Message cannot be null or empty.", nameof(htmlMessage));
        }
    }
}
