using SportRental.Shared.Models;

namespace SportRental.Admin.Payments;

internal sealed record CheckoutRentalPayload
{
    public int SchemaVersion { get; init; } = 1;
    public CheckoutCustomerSnapshot Customer { get; init; } = new();
    public DateTime StartDateUtc { get; init; }
    public DateTime EndDateUtc { get; init; }
    public List<CheckoutTenantPayload> Tenants { get; init; } = new();
    public string? Notes { get; init; }
    public string IdempotencyKey { get; init; } = string.Empty;
    public decimal TotalAmount { get; init; }
    public decimal DepositAmount { get; init; }
    public RentalTypeDto RentalType { get; init; } = RentalTypeDto.Daily;
    public int? HoursRented { get; init; }
    public List<Guid> HoldIds { get; init; } = new();
    public string AcceptedTermsVersion { get; init; } = string.Empty;
    public string AcknowledgedPrivacyVersion { get; init; } = string.Empty;
}

internal sealed record CheckoutTenantPayload
{
    public int Sequence { get; init; }
    public Guid TenantId { get; init; }
    public string TenantName { get; init; } = string.Empty;
    public DateTime StartDateUtc { get; init; }
    public DateTime EndDateUtc { get; init; }
    public RentalTypeDto RentalType { get; init; } = RentalTypeDto.Daily;
    public int? HoursRented { get; init; }
    public List<CheckoutRentalItemPayload> Items { get; init; } = new();
    public decimal TotalAmount { get; init; }
    public decimal DepositAmount { get; init; }
    public string? RegulationsTextSnapshot { get; init; }
    public string RegulationsHash { get; init; } = string.Empty;
    public string RegulationsVersion { get; init; } = string.Empty;
    public string RegulationsSource { get; init; } = string.Empty;
}

internal sealed record CheckoutRentalItemPayload
{
    public Guid ProductId { get; init; }
    public int Quantity { get; init; }
    public decimal PricePerDay { get; init; }
    public decimal? PricePerHour { get; init; }
    public decimal Subtotal { get; init; }
}

internal sealed record CheckoutCustomerSnapshot
{
    public Guid CustomerId { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string? Email { get; init; }
    public string? PhoneNumber { get; init; }
    public string? Address { get; init; }
    public string? DocumentNumber { get; init; }
}
