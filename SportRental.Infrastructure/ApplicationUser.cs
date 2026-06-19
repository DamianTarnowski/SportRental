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
}