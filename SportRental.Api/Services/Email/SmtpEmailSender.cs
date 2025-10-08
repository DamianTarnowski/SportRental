using System.Net.Mail;
using MimeKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using SmtpClient = MailKit.Net.Smtp.SmtpClient;

namespace SportRental.Api.Services.Email;

/// <summary>
/// SMTP-based email sender using MailKit
/// </summary>
public class SmtpEmailSender : IEmailSender
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IConfiguration configuration, ILogger<SmtpEmailSender> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        await SendEmailWithAttachmentAsync(email, subject, htmlMessage);
    }

    public async Task SendEmailWithAttachmentAsync(string email, string subject, string htmlMessage, string? attachmentPath = null)
    {
        // Validation
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email address cannot be null or empty.", nameof(email));
        if (string.IsNullOrWhiteSpace(subject))
            throw new ArgumentException("Subject cannot be null or empty.", nameof(subject));
        if (string.IsNullOrWhiteSpace(htmlMessage))
            throw new ArgumentException("Message cannot be null or empty.", nameof(htmlMessage));

        EnsureValidEmail(email);

        try
        {
            var smtpSettings = GetSmtpSettings();
            
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(smtpSettings.SenderName, smtpSettings.SenderEmail));
            message.To.Add(new MailboxAddress("", email));
            message.Subject = subject;

            var bodyBuilder = new BodyBuilder();
            
            // Check if htmlMessage contains HTML
            if (htmlMessage.Contains("<html>") || htmlMessage.Contains("<p>") || htmlMessage.Contains("<br"))
            {
                bodyBuilder.HtmlBody = htmlMessage;
            }
            else
            {
                bodyBuilder.TextBody = htmlMessage;
            }

            // Attach file if exists
            if (!string.IsNullOrEmpty(attachmentPath) && File.Exists(attachmentPath))
            {
                bodyBuilder.Attachments.Add(attachmentPath);
            }

            message.Body = bodyBuilder.ToMessageBody();

            using var client = new SmtpClient();
            
            // Port 465 requires SSL on connect, port 587 uses STARTTLS
            var secureSocketOptions = smtpSettings.Port == 465 
                ? SecureSocketOptions.SslOnConnect 
                : smtpSettings.EnableSsl 
                    ? SecureSocketOptions.StartTls 
                    : SecureSocketOptions.None;
            
            _logger.LogInformation("Connecting to SMTP: {Host}:{Port} with SSL={SSL}", 
                smtpSettings.Host, smtpSettings.Port, secureSocketOptions);
            
            await client.ConnectAsync(smtpSettings.Host, smtpSettings.Port, secureSocketOptions);
            
            _logger.LogInformation("Connected successfully. Authenticating as {Username}...", smtpSettings.Username);
            
            if (!string.IsNullOrEmpty(smtpSettings.Username))
            {
                await client.AuthenticateAsync(smtpSettings.Username, smtpSettings.Password);
                _logger.LogInformation("Authentication successful for {Username}", smtpSettings.Username);
            }

            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            _logger.LogInformation("Email sent successfully to {Email}", email);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending email to {Email}", email);
            throw;
        }
    }

    private static void EnsureValidEmail(string email)
    {
        if (!MailAddress.TryCreate(email, out _))
        {
            throw new ArgumentException("Invalid email format.", nameof(email));
        }
    }

    private SmtpSettings GetSmtpSettings()
    {
        var enabled = _configuration.GetValue<bool?>("Email:Smtp:Enabled") ?? false;
        if (!enabled)
        {
            throw new InvalidOperationException("SMTP email is not enabled in configuration. Set Email:Smtp:Enabled = true");
        }

        return new SmtpSettings
        {
            Host = _configuration["Email:Smtp:Host"] ?? "localhost",
            Port = int.Parse(_configuration["Email:Smtp:Port"] ?? "587"),
            EnableSsl = bool.Parse(_configuration["Email:Smtp:EnableSsl"] ?? "true"),
            Username = _configuration["Email:Smtp:Username"],
            Password = _configuration["Email:Smtp:Password"],
            SenderEmail = _configuration["Email:Smtp:SenderEmail"] ?? "sportrental@localhost",
            SenderName = _configuration["Email:Smtp:SenderName"] ?? "SportRental"
        };
    }

    private class SmtpSettings
    {
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; }
        public bool EnableSsl { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
        public string SenderEmail { get; set; } = string.Empty;
        public string SenderName { get; set; } = string.Empty;
    }
}
