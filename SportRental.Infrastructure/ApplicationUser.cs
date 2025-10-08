using Microsoft.AspNetCore.Identity;

namespace SportRental.Infrastructure.Data;

public class ApplicationUser : IdentityUser<Guid>
{
    /// <summary>
    /// Optional tenant scope assigned to the user for multi-tenant queries.
    /// </summary>
    public Guid? TenantId { get; set; }
}