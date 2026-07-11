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
            // Walidacja parametrów
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
                
                // Sprawdź czy htmlMessage zawiera HTML
                if (htmlMessage.Contains("<html>") || htmlMessage.Contains("<p>") || htmlMessage.Contains("<br"))
                {
                    bodyBuilder.HtmlBody = htmlMessage;
                }
                else
                {
                    bodyBuilder.TextBody = htmlMessage;
                }

                // Dołącz załącznik jeśli istnieje
                if (!string.IsNullOrEmpty(attachmentPath) && File.Exists(attachmentPath))
                {
                    bodyBuilder.Attachments.Add(attachmentPath);
                }

                message.Body = bodyBuilder.ToMessageBody();

                using var client = new SmtpClient();
                // Port 587 = STARTTLS, Port 465 = SSL
                var secureOption = smtpSettings.Port == 587 
                    ? MailKit.Security.SecureSocketOptions.StartTls 
                    : (smtpSettings.UseSsl ? MailKit.Security.SecureSocketOptions.SslOnConnect : MailKit.Security.SecureSocketOptions.Auto);
                await client.ConnectAsync(smtpSettings.Host, smtpSettings.Port, secureOption);
                
                await AuthenticateIfConfiguredAsync(client, smtpSettings);

                await client.SendAsync(message);
                await client.DisconnectAsync(true);

                _logger.LogInformation("Email wysłany pomyślnie do {Email}", email);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Błąd podczas wysyłania emaila do {Email}", email);
                throw;
            }
        }

        public async Task SendRentalContractAsync(string email, string customerName, byte[] contractPdf)
        {
            // Walidacja parametrów
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
                message.Subject = "Nowa umowa wypożyczenia SportRental";

                var bodyBuilder = new BodyBuilder();
                bodyBuilder.HtmlBody = $@"
                    <h2>Dzień dobry {customerName}!</h2>
                    <p>W załączniku znajduje się umowa wypożyczenia sprzętu sportowego.</p>
                    <p>Prosimy o zapoznanie się z treścią umowy.</p>
                    <br>
                    <p>Pozdrawiamy,<br>
                    Zespół SportRental</p>";

                // Dołącz PDF jako załącznik
                bodyBuilder.Attachments.Add("umowa_najmu.pdf", contractPdf, ContentType.Parse("application/pdf"));

                message.Body = bodyBuilder.ToMessageBody();

                using var client = new SmtpClient();
                // Port 587 = STARTTLS, Port 465 = SSL
                var secureOption = smtpSettings.Port == 587 
                    ? MailKit.Security.SecureSocketOptions.StartTls 
                    : (smtpSettings.UseSsl ? MailKit.Security.SecureSocketOptions.SslOnConnect : MailKit.Security.SecureSocketOptions.Auto);
                await client.ConnectAsync(smtpSettings.Host, smtpSettings.Port, secureOption);
                
                await AuthenticateIfConfiguredAsync(client, smtpSettings);

                await client.SendAsync(message);
                await client.DisconnectAsync(true);

                _logger.LogInformation("Umowa wysłana emailem do {Email} dla klienta {CustomerName}", email, customerName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Błąd podczas wysyłania umowy do {Email}", email);
                throw;
            }
        }

        public async Task SendReminderAsync(string email, string customerName, string reminderText)
        {
            // Walidacja parametrów
            if (string.IsNullOrWhiteSpace(email))
                throw new ArgumentException("Email address cannot be null or empty.", nameof(email));
            if (string.IsNullOrWhiteSpace(customerName))
                throw new ArgumentException("Customer name cannot be null or empty.", nameof(customerName));
            if (string.IsNullOrWhiteSpace(reminderText))
                throw new ArgumentException("Reminder text cannot be null or empty.", nameof(reminderText));

            var subject = "Przypomnienie o zwrocie sprzętu - SportRental";
            var htmlBody = $@"
                <h2>Dzień dobry {customerName}!</h2>
                <p>{reminderText}</p>
                <br>
                <p>Pozdrawiamy,<br>
                Zespół SportRental</p>";

            await SendEmailAsync(email, subject, htmlBody);
        }

        public async Task SendReturnThankYouAsync(string email, string customerName, string? reviewUrl, string? optOutUrl, string? companyName)
        {
            if (string.IsNullOrWhiteSpace(email)) throw new ArgumentException("Email address cannot be null or empty.", nameof(email));
            if (string.IsNullOrWhiteSpace(customerName)) throw new ArgumentException("Customer name cannot be null or empty.", nameof(customerName));

            var company = string.IsNullOrWhiteSpace(companyName) ? "SportRental" : companyName;
            var subject = $"Dziękujemy za skorzystanie z naszych usług - {company}";

            var reviewCta = string.IsNullOrEmpty(reviewUrl)
                ? string.Empty
                : $@"<p style=""margin:24px 0;"">
                        <a href=""{reviewUrl}""
                           style=""display:inline-block;padding:14px 26px;background:linear-gradient(135deg,#f093fb 0%,#f5576c 100%);color:#fff;text-decoration:none;border-radius:8px;font-weight:700;font-size:16px;"">
                            ⭐ Wystaw opinię
                        </a>
                     </p>";

            var optOutBlock = string.IsNullOrEmpty(optOutUrl)
                ? string.Empty
                : $@"<p style=""font-size:12px;color:#777;margin-top:24px;"">
                        Jeśli nie chcesz otrzymywać od nas maili z prośbą o opinię,
                        <a href=""{optOutUrl}"" style=""color:#777;"">zrezygnuj tutaj</a>.
                     </p>";

            var htmlBody = $@"
                <div style=""font-family:sans-serif;max-width:560px;margin:0 auto;"">
                    <h2 style=""color:#1f2937;"">Dzień dobry {customerName}!</h2>
                    <p>Potwierdzamy przyjęcie zwrotu sprzętu. <strong>Dziękujemy, że wybrałeś/aś {company}</strong> — mamy nadzieję, że wypożyczenie spełniło Twoje oczekiwania.</p>
                    <p>Jeśli masz chwilę, prosimy o krótką opinię — 3 oceny (jakość sprzętu, cena, obsługa) i opcjonalny komentarz. Twoja opinia pomaga nam się rozwijać i innym klientom wybrać dobrze.</p>
                    {reviewCta}
                    <p>Pozdrawiamy,<br/>Zespół {company}</p>
                    {optOutBlock}
                </div>";

            await SendEmailAsync(email, subject, htmlBody);
        }

        private static void EnsureValidEmail(string email)
        {
            if (!MailAddress.TryCreate(email, out _))
            {
                throw new ArgumentException("Invalid email format.", nameof(email));
            }
        }

        private static Task AuthenticateIfConfiguredAsync(SmtpClient client, SmtpSettings settings)
        {
            if (string.IsNullOrEmpty(settings.Username))
            {
                return Task.CompletedTask;
            }

            if (string.IsNullOrEmpty(settings.Password))
            {
                throw new InvalidOperationException(
                    "Email:Smtp:Password is required when Email:Smtp:Username is configured.");
            }

            return client.AuthenticateAsync(settings.Username, settings.Password);
        }

        private SmtpSettings GetSmtpSettings()
        {
            return new SmtpSettings
            {
                Host = _configuration["Email:Smtp:Host"] ?? "localhost",
                Port = int.Parse(_configuration["Email:Smtp:Port"] ?? "587"),
                UseSsl = bool.Parse(_configuration["Email:Smtp:UseSsl"] ?? _configuration["Email:Smtp:EnableSsl"] ?? "true"),
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
            public bool UseSsl { get; set; }
            public string? Username { get; set; }
            public string? Password { get; set; }
            public string SenderEmail { get; set; } = string.Empty;
            public string SenderName { get; set; } = string.Empty;
        }
    }
}

