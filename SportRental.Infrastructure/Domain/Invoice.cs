using System.ComponentModel.DataAnnotations;

namespace SportRental.Infrastructure.Domain;

/// <summary>
/// Faza 8c (Bookero parity #3): faktura VAT generowana z wynajmu.
/// Numer: FV/{YYYY}/{NNNN} per tenant per rok (atomic counter w InvoiceCounter).
/// </summary>
public class Invoice
{
    [Key]
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid RentalId { get; set; }
    public Guid CustomerId { get; set; }

    /// <summary>Format FV/{YYYY}/{NNNN} np. FV/2026/0042. Unique per tenant.</summary>
    [Required, MaxLength(40)]
    public string Number { get; set; } = string.Empty;

    public DateTime IssuedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime DueAtUtc { get; set; } = DateTime.UtcNow.AddDays(14);

    public decimal NetAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal GrossAmount { get; set; }

    /// <summary>"23%", "8%", "zw." — bez znaku.</summary>
    [Required, MaxLength(10)]
    public string VatRate { get; set; } = "23%";

    public InvoiceStatus Status { get; set; } = InvoiceStatus.Issued;

    /// <summary>Blob storage path do PDF, jeśli wygenerowany.</summary>
    [MaxLength(512)]
    public string? PdfUrl { get; set; }

    public Rental? Rental { get; set; }
    public Customer? Customer { get; set; }
    public Tenant? Tenant { get; set; }
}

public enum InvoiceStatus
{
    Draft = 0,
    Issued = 1,
    Paid = 2,
    Cancelled = 3
}

/// <summary>
/// Atomic counter per tenant per rok dla numerów faktur (FV/{YYYY}/{NNNN}).
/// PostgreSQL: UPDATE ... RETURNING NextNumber atomic.
/// </summary>
public class InvoiceCounter
{
    [Key]
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public int Year { get; set; }

    /// <summary>Następna do użycia liczba — incrementowana atomic per wystawienie.</summary>
    public long NextNumber { get; set; } = 1;
}
