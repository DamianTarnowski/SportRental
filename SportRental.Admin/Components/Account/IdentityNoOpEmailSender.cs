using SportRental.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;

namespace SportRental.Admin.Components.Account
{
    internal sealed class IdentityNoOpEmailSender : IEmailSender<ApplicationUser>
    {
        private readonly IEmailSender _emailSender;

        public IdentityNoOpEmailSender(IEmailSender emailSender)
        {
            _emailSender = emailSender;
        }

        public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink) =>
            _emailSender.SendEmailAsync(email, "Potwierdź swój email", 
                $"<h2>Witaj!</h2><p>Proszę potwierdź swoje konto <a href='{confirmationLink}'>klikając tutaj</a>.</p><p>Pozdrawiamy,<br>Zespół SportRental</p>");

        public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink) =>
            _emailSender.SendEmailAsync(email, "Resetowanie hasła", 
                $"<h2>Resetowanie hasła</h2><p>Aby zresetować hasło <a href='{resetLink}'>kliknij tutaj</a>.</p><p>Pozdrawiamy,<br>Zespół SportRental</p>");

        public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode) =>
            _emailSender.SendEmailAsync(email, "Kod resetowania hasła", 
                $"<h2>Kod resetowania hasła</h2><p>Twój kod resetowania hasła: <strong>{resetCode}</strong></p><p>Pozdrawiamy,<br>Zespół SportRental</p>");
    }
}
