using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using SportRental.Infrastructure.Data;
using SportRental.Shared.Identity;
using SportRental.Shared.Legal;

namespace SportRental.Admin.Services.Auth;

public enum ExternalLoginProvisioningStatus
{
    Created,
    ExistingEmailCollision,
    LegalAcceptanceRequired,
    MissingOrInvalidEmail,
    Failed
}

public sealed record ExternalLoginProvisioningResult(
    ExternalLoginProvisioningStatus Status,
    ApplicationUser? User = null);

/// <summary>
/// Tworzy konto dla nieznanego loginu zewnętrznego. Istniejącego konta lokalnego
/// nigdy nie łączy automatycznie wyłącznie na podstawie zgodnego adresu email.
/// </summary>
public sealed class ExternalLoginProvisioningService(
    UserManager<ApplicationUser> userManager,
    ILogger<ExternalLoginProvisioningService> logger)
{
    public async Task<ExternalLoginProvisioningResult> ProvisionAsync(
        ExternalLoginInfo loginInfo,
        string? acceptedTermsVersion = null,
        string? acknowledgedPrivacyVersion = null)
    {
        if (!string.Equals(acceptedTermsVersion, LegalDocumentVersions.Terms, StringComparison.Ordinal) ||
            !string.Equals(acknowledgedPrivacyVersion, LegalDocumentVersions.Privacy, StringComparison.Ordinal))
        {
            return new(ExternalLoginProvisioningStatus.LegalAcceptanceRequired);
        }

        var emailClaim = loginInfo.Principal.FindFirstValue(ClaimTypes.Email);
        if (!PublicAuthInputValidator.TryNormalizeEmail(emailClaim, out var email))
        {
            return new(ExternalLoginProvisioningStatus.MissingOrInvalidEmail);
        }

        var existingUser = await userManager.FindByEmailAsync(email);
        if (existingUser is not null)
        {
            logger.LogWarning(
                "Odrzucono automatyczne połączenie istniejącego konta z loginem {Provider}.",
                loginInfo.LoginProvider);
            return new(ExternalLoginProvisioningStatus.ExistingEmailCollision);
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            // Adres pochodzi z uwierzytelnionej tożsamości Google. Bezpieczeństwo
            // logowania opiera się na ProviderKey, a nie na dopasowaniu emaila.
            EmailConfirmed = string.Equals(loginInfo.LoginProvider, "Google", StringComparison.OrdinalIgnoreCase),
            AcceptedTermsVersion = LegalDocumentVersions.Terms,
            AcknowledgedPrivacyVersion = LegalDocumentVersions.Privacy,
            LegalAcceptedAtUtc = DateTime.UtcNow
        };

        var createResult = await userManager.CreateAsync(user);
        if (!createResult.Succeeded)
        {
            LogIdentityFailure("utworzenia konta", loginInfo.LoginProvider, createResult);
            return new(ExternalLoginProvisioningStatus.Failed);
        }

        var addLoginResult = await userManager.AddLoginAsync(user, loginInfo);
        if (!addLoginResult.Succeeded)
        {
            LogIdentityFailure("dodania loginu zewnętrznego", loginInfo.LoginProvider, addLoginResult);
            await DeleteIncompleteUserAsync(user);
            return new(ExternalLoginProvisioningStatus.Failed);
        }

        var roleResult = await userManager.AddToRoleAsync(user, RoleNames.Client);
        if (!roleResult.Succeeded)
        {
            LogIdentityFailure("nadania roli klienta", loginInfo.LoginProvider, roleResult);
            await DeleteIncompleteUserAsync(user);
            return new(ExternalLoginProvisioningStatus.Failed);
        }

        return new(ExternalLoginProvisioningStatus.Created, user);
    }

    private void LogIdentityFailure(string operation, string provider, IdentityResult result)
    {
        logger.LogError(
            "Błąd {Operation} dla providera {Provider}. Kody: {Codes}",
            operation,
            provider,
            string.Join(",", result.Errors.Select(error => error.Code)));
    }

    private async Task DeleteIncompleteUserAsync(ApplicationUser user)
    {
        var deleteResult = await userManager.DeleteAsync(user);
        if (!deleteResult.Succeeded)
        {
            logger.LogError(
                "Nie udało się usunąć niekompletnego konta zewnętrznego. Kody: {Codes}",
                string.Join(",", deleteResult.Errors.Select(error => error.Code)));
        }
    }
}
