using Microsoft.AspNetCore.Identity;

namespace SportRental.Infrastructure.Data;

public class ApplicationUser : IdentityUser<Guid>
{
    /// <summary>
    /// Optional tenant scope assigned to the user for multi-tenant queries.
    /// </summary>
    public Guid? TenantId { get; set; }

    /// <summary>
    /// Demo użytkownik — utworzony per session na kliknięciu "Wypróbuj demo".
    /// SMS/Email są blokowane / kierowane na sandbox; cleanup usuwa po wygaśnięciu tenanta.
    /// </summary>
    public bool IsDemoUser { get; set; }

    /// <summary>
    /// Wersja regulaminu platformy zaakceptowana podczas ostatniej wymaganej akceptacji.
    /// </summary>
    public string? AcceptedTermsVersion { get; set; }

    /// <summary>
    /// Wersja polityki prywatności, z którą użytkownik potwierdził zapoznanie się.
    /// Polityka prywatności jest informacją, a nie zgodą na przetwarzanie niezbędne do umowy.
    /// </summary>
    public string? AcknowledgedPrivacyVersion { get; set; }

    /// <summary>
    /// Czas zapisania powyższego oświadczenia, zawsze w UTC.
    /// </summary>
    public DateTime? LegalAcceptedAtUtc { get; set; }
}
