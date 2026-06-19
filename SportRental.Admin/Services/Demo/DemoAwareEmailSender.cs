using SportRental.Admin.Services.Email;

namespace SportRental.Admin.Services.Demo;

/// Decorator nad IEmailSender — w trybie demo NIE wysyła emaili, tylko loguje "DEMO SANDBOX".
/// Inne tryby — passthrough do real sender (SMTP / NoOp).
public class DemoAwareEmailSender : IEmailSender
{
    private readonly IEmailSender _inner;
    private readonly IDemoGuard _guard;
    private readonly ILogger<DemoAwareEmailSender> _logger;

    public DemoAwareEmailSender(IEmailSender inner, IDemoGuard guard, ILogger<DemoAwareEmailSender> logger)
    {
        _inner = inner;
        _guard = guard;
        _logger = logger;
    }

    private async Task<bool> IsDemoAsync()
    {
        try { return await _guard.IsCurrentTenantDemoAsync(); }
        catch { return false; }
    }

    public async Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        if (await IsDemoAsync())
        {
            _logger.LogInformation("DEMO SANDBOX: email to {Email} subject='{Subject}'", email, subject);
            return;
        }
        await _inner.SendEmailAsync(email, subject, htmlMessage);
    }

    public async Task SendEmailWithAttachmentAsync(string email, string subject, string htmlMessage, string? attachmentPath = null)
    {
        if (await IsDemoAsync())
        {
            _logger.LogInformation("DEMO SANDBOX: email+attachment to {Email} subject='{Subject}' attachment={Path}", email, subject, attachmentPath);
            return;
        }
        await _inner.SendEmailWithAttachmentAsync(email, subject, htmlMessage, attachmentPath);
    }

    public async Task SendRentalContractAsync(string email, string customerName, byte[] contractPdf)
    {
        if (await IsDemoAsync())
        {
            _logger.LogInformation("DEMO SANDBOX: contract pdf to {Email} ({Name}) size={Bytes}b", email, customerName, contractPdf.Length);
            return;
        }
        await _inner.SendRentalContractAsync(email, customerName, contractPdf);
    }

    public async Task SendReminderAsync(string email, string customerName, string reminderText)
    {
        if (await IsDemoAsync())
        {
            _logger.LogInformation("DEMO SANDBOX: reminder to {Email} ({Name})", email, customerName);
            return;
        }
        await _inner.SendReminderAsync(email, customerName, reminderText);
    }

    public async Task SendReturnThankYouAsync(string email, string customerName, string? reviewUrl, string? optOutUrl, string? companyName)
    {
        if (await IsDemoAsync())
        {
            _logger.LogInformation("DEMO SANDBOX: thank-you to {Email} ({Name}) reviewUrl={Url}", email, customerName, reviewUrl);
            return;
        }
        await _inner.SendReturnThankYouAsync(email, customerName, reviewUrl, optOutUrl, companyName);
    }
}
