using System.ComponentModel.DataAnnotations;

namespace SportRental.Infrastructure.Domain;

/// <summary>
/// Faza 8b (Bookero parity): cennik sezonowy per produkt.
/// Resolver wybiera najwyższy priorytet AKTYWNY rule w danym dniu wynajmu.
/// Brak rule = bazowa Product.DailyPrice.
/// </summary>
public class PriceRule
{
    [Key]
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProductId { get; set; }

    [Required, MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }

    public PriceRuleType Type { get; set; }

    /// <summary>
    /// Wartość modyfikatora. Dla Multiplier: 1.5 = +50%. Dla FixedAdd: dodawana złotówka.
    /// Dla FixedReplace: nadpisuje bazową cenę całkowicie.
    /// </summary>
    public decimal Value { get; set; }

    public int Priority { get; set; }
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Product? Product { get; set; }
    public Tenant? Tenant { get; set; }
}

public enum PriceRuleType
{
    /// <summary>basePrice × Value (np. 1.5 = wysoki sezon +50%).</summary>
    Multiplier = 0,

    /// <summary>basePrice + Value (np. +50 PLN za dzień świąteczny).</summary>
    FixedAdd = 1,

    /// <summary>Value (zastępuje bazową cenę całkowicie).</summary>
    FixedReplace = 2
}
