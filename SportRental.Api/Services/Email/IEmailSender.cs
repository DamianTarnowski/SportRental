namespace SportRental.Api.Services.Email;

/// <summary>
/// Service for sending emails
/// </summary>
public interface IEmailSender
{
    /// <summary>
    /// Send email with HTML content
    /// </summary>
    Task SendEmailAsync(string email, string subject, string htmlMessage);

    /// <summary>
    /// Send email with attachment
    /// </summary>
    Task SendEmailWithAttachmentAsync(string email, string subject, string htmlMessage, string? attachmentPath = null);
}
