using Microsoft.AspNetCore.Identity.UI.Services;

namespace SportRental.Admin.Services.Email
{
    public interface IEmailSender : Microsoft.AspNetCore.Identity.UI.Services.IEmailSender
    {
        Task SendEmailWithAttachmentAsync(string email, string subject, string htmlMessage, string? attachmentPath = null);
        Task SendRentalContractAsync(string email, string customerName, byte[] contractPdf);
        Task SendReminderAsync(string email, string customerName, string reminderText);
    }
}