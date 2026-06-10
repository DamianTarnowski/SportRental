using System.ComponentModel.DataAnnotations;

namespace SportRental.Infrastructure.Domain;

/// <summary>
/// Faza 9a (Bookero parity): kod rabatowy. Klient wpisuje przy checkout,
/// system odejmuje % lub kwotę od total. Limit użyć + okres ważności + min order.
/// </summary>
public class DiscountCode
{
    [Key]
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    /// <summary>Kod uppercase, unique per tenant. Np. "LATO2026", "PARTNER20".</summary>
    [Required, MaxLength(40)]
    public string Code { get; set; } = string.Empty;

    public DiscountType Type { get; set; }

    /// <summary>Dla Percentage: 0-100 (10 = -10%). Dla FixedAmount: kwota w PLN.</summary>
    public decimal Value { get; set; }

    public DateOnly? ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }

    /// <summary>Null = unlimited.</summary>
    public int? MaxUses { get; set; }

    public int UsedCount { get; set; }

    /// <summary>Wymagana minimalna kwota brutto żeby kod zadziałał. Null = brak limitu.</summary>
    public decimal? MinOrderAmount { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    [MaxLength(256)]
    public string? Description { get; set; }

    public Tenant? Tenant { get; set; }
}

public enum DiscountType
{
    /// <summary>Procentowy: Value 10 = -10% od totalu.</summary>
    Percentage = 0,
    /// <summary>Kwotowy: Value 50 = -50 zł od totalu.</summary>
    FixedAmount = 1
}

/// <summary>
/// Audit + zabezpieczenie przed wielokrotnym użyciem na tym samym rentalu.
/// Unique (DiscountCodeId, RentalId).
/// </summary>
public class DiscountRedemption
{
    [Key]
    public Guid Id { get; set; }
    public Guid DiscountCodeId { get; set; }
    public Guid RentalId { get; set; }
    public Guid TenantId { get; set; }
    public DateTime RedeemedAtUtc { get; set; } = DateTime.UtcNow;
    public decimal AppliedAmount { get; set; }

    public DiscountCode? DiscountCode { get; set; }
    public Rental? Rental { get; set; }
}
