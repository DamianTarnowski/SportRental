using System.ComponentModel.DataAnnotations;

namespace SportRental.Infrastructure.Domain;

/// <summary>
/// Faza 9b (Bookero parity): voucher / karta podarunkowa. Klient kupuje
/// w wypożyczalni (jako produkt) albo dostaje od admina. Saldo można wykorzystać
/// stopniowo na wiele wynajmów. Kod 16-znakowy globalnie unique.
/// </summary>
public class Voucher
{
    [Key]
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    /// <summary>VCH-XXXX-XXXX-XXXX (16 znaków + 3 myślniki). Globalnie unique.</summary>
    [Required, MaxLength(24)]
    public string Code { get; set; } = string.Empty;

    [MaxLength(128)]
    public string? IssuedToName { get; set; }

    [MaxLength(256)]
    public string? IssuedToEmail { get; set; }

    public decimal InitialBalance { get; set; }
    public decimal RemainingBalance { get; set; }

    public DateOnly? ExpiresAt { get; set; }

    public VoucherStatus Status { get; set; } = VoucherStatus.Active;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Guid? CreatedByUserId { get; set; }

    /// <summary>Jeśli voucher był produktem kupionym, wskazuje rental zakupu.</summary>
    public Guid? PurchasedByRentalId { get; set; }

    [MaxLength(512)]
    public string? Notes { get; set; }

    public Tenant? Tenant { get; set; }
}

public enum VoucherStatus
{
    Active = 0,
    FullyRedeemed = 1,
    Expired = 2,
    Cancelled = 3
}

public class VoucherRedemption
{
    [Key]
    public Guid Id { get; set; }
    public Guid VoucherId { get; set; }
    public Guid RentalId { get; set; }
    public Guid TenantId { get; set; }
    public DateTime RedeemedAtUtc { get; set; } = DateTime.UtcNow;
    public decimal Amount { get; set; }

    public Voucher? Voucher { get; set; }
    public Rental? Rental { get; set; }
}
