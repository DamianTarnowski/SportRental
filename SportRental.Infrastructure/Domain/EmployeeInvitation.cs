using System.ComponentModel.DataAnnotations;

namespace SportRental.Infrastructure.Domain;

/// <summary>
/// Zaproszenie pracownika do wypożyczalni - wysyłane przez Owner do nowego pracownika.
/// Analogiczne do TenantInvitation, ale dla pracowników w ramach istniejącego tenanta.
/// </summary>
public class EmployeeInvitation
{
    public Guid Id { get; set; }
    
    /// <summary>
    /// Tenant (wypożyczalnia) do której zapraszany jest pracownik
    /// </summary>
    public Guid TenantId { get; set; }
    
    [Required]
    [MaxLength(256)]
    public string Email { get; set; } = string.Empty;
    
    /// <summary>
    /// Opcjonalna sugerowana nazwa pracownika
    /// </summary>
    [MaxLength(200)]
    public string? FullName { get; set; }
    
    /// <summary>
    /// Rola pracownika (Pracownik, Kierownik, Manager)
    /// </summary>
    public EmployeeRole Role { get; set; } = EmployeeRole.Pracownik;
    
    [Required]
    [MaxLength(128)]
    public string Token { get; set; } = string.Empty;
    
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    
    public DateTime ExpiresAtUtc { get; set; }
    
    public bool IsUsed { get; set; }
    
    public DateTime? UsedAtUtc { get; set; }
    
    /// <summary>
    /// ID utworzonego pracownika po wykorzystaniu zaproszenia
    /// </summary>
    public Guid? CreatedEmployeeId { get; set; }
    
    /// <summary>
    /// ID użytkownika który wysłał zaproszenie (Owner)
    /// </summary>
    public Guid? InvitedByUserId { get; set; }
    
    [MaxLength(500)]
    public string? Notes { get; set; }
}
