using System.ComponentModel.DataAnnotations;

namespace SportRental.Infrastructure.Domain;

public class TenantInvitation
{
    public Guid Id { get; set; }
    
    [Required]
    [MaxLength(256)]
    public string Email { get; set; } = string.Empty;
    
    [MaxLength(200)]
    public string? TenantName { get; set; }
    
    [Required]
    [MaxLength(128)]
    public string Token { get; set; } = string.Empty;
    
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    
    public DateTime ExpiresAtUtc { get; set; }
    
    public bool IsUsed { get; set; }
    
    public DateTime? UsedAtUtc { get; set; }
    
    public Guid? CreatedTenantId { get; set; }
    
    [MaxLength(500)]
    public string? Notes { get; set; }
}

