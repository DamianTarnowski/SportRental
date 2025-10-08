using System.Net;
using System.Net.Mail;
using MimeKit;
using MailKit.Net.Smtp;
using SmtpClient = MailKit.Net.Smtp.SmtpClient;
using MailMessage = System.Net.Mail.MailMessage;
using SportRental.Admin.Services.Email;

namespace SportRental.Admin.Services.Email
{
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
            // Walidacja parametrĂłw
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
                
                // SprawdĹş czy htmlMessage zawiera HTML
                if (htmlMessage.Contains("<html>") || htmlMessage.Contains("<p>") || htmlMessage.Contains("<br"))
                {
                    bodyBuilder.HtmlBody = htmlMessage;
                }
                else
                {
                    bodyBuilder.TextBody = htmlMessage;
                }

                // DoĹ‚Ä…cz zaĹ‚Ä…cznik jeĹ›li istnieje
                if (!string.IsNullOrEmpty(attachmentPath) && File.Exists(attachmentPath))
                {
                    bodyBuilder.Attachments.Add(attachmentPath);
                }

                message.Body = bodyBuilder.ToMessageBody();

                using var client = new SmtpClient();
                await client.ConnectAsync(smtpSettings.Host, smtpSettings.Port, smtpSettings.EnableSsl);
                
                if (!string.IsNullOrEmpty(smtpSettings.Username))
                {
                    await client.AuthenticateAsync(smtpSettings.Username, smtpSettings.Password);
                }

                await client.SendAsync(message);
                await client.DisconnectAsync(true);

                _logger.LogInformation("Email wysĹ‚any pomyĹ›lnie do {Email}", email);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BĹ‚Ä…d podczas wysyĹ‚ania emaila do {Email}", email);
                throw;
            }
        }

        public async Task SendRentalContractAsync(string email, string customerName, byte[] contractPdf)
        {
            // Walidacja parametrĂłw
            if (string.IsNullOrWhiteSpace(email))
                throw new ArgumentException("Email address cannot be null or empty.", nameof(email));
            if (string.IsNullOrWhiteSpace(customerName))
                throw new ArgumentException("Customer name cannot be null or empty.", nameof(customerName));
            if (contractPdf == null || contractPdf.Length == 0)
                throw new ArgumentException("Contract PDF cannot be null or empty.", nameof(contractPdf));

            EnsureValidEmail(email);

            try
            {
                var smtpSettings = GetSmtpSettings();
                
                var message = new MimeMessage();
                message.From.Add(new MailboxAddress(smtpSettings.SenderName, smtpSettings.SenderEmail));
                message.To.Add(new MailboxAddress(customerName, email));
                message.Subject = "Nowa umowa wypoĹĽyczenia SportRental";

                var bodyBuilder = new BodyBuilder();
                bodyBuilder.HtmlBody = $@"
                    <h2>DzieĹ„ dobry {customerName}!</h2>
                    <p>W zaĹ‚Ä…czniku znajduje siÄ™ umowa wypoĹĽyczenia sprzÄ™tu sportowego.</p>
                    <p>Prosimy o zapoznanie siÄ™ z treĹ›ciÄ… umowy.</p>
                    <br>
                    <p>Pozdrawiamy,<br>
                    ZespĂłĹ‚ SportRental</p>";

                // DoĹ‚Ä…cz PDF jako zaĹ‚Ä…cznik
                bodyBuilder.Attachments.Add("umowa_najmu.pdf", contractPdf, ContentType.Parse("application/pdf"));

                message.Body = bodyBuilder.ToMessageBody();

                using var client = new SmtpClient();
                await client.ConnectAsync(smtpSettings.Host, smtpSettings.Port, smtpSettings.EnableSsl);
                
                if (!string.IsNullOrEmpty(smtpSettings.Username))
                {
                    await client.AuthenticateAsync(smtpSettings.Username, smtpSettings.Password);
                }

                await client.SendAsync(message);
                await client.DisconnectAsync(true);

                _logger.LogInformation("Umowa wysĹ‚ana emailem do {Email} dla klienta {CustomerName}", email, customerName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BĹ‚Ä…d podczas wysyĹ‚ania umowy do {Email}", email);
                throw;
            }
        }

        public async Task SendReminderAsync(string email, string customerName, string reminderText)
        {
            // Walidacja parametrĂłw
            if (string.IsNullOrWhiteSpace(email))
                throw new ArgumentException("Email address cannot be null or empty.", nameof(email));
            if (string.IsNullOrWhiteSpace(customerName))
                throw new ArgumentException("Customer name cannot be null or empty.", nameof(customerName));
            if (string.IsNullOrWhiteSpace(reminderText))
                throw new ArgumentException("Reminder text cannot be null or empty.", nameof(reminderText));

            var subject = "Przypomnienie o zwrocie sprzÄ™tu - SportRental";
            var htmlBody = $@"
                <h2>DzieĹ„ dobry {customerName}!</h2>
                <p>{reminderText}</p>
                <br>
                <p>Pozdrawiamy,<br>
                ZespĂłĹ‚ SportRental</p>";

            await SendEmailAsync(email, subject, htmlBody);
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
}

