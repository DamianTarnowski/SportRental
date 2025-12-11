using SportRental.Shared.Models;

namespace SportRental.Admin.Payments;

internal sealed record CheckoutRentalPayload
{
    public CheckoutCustomerSnapshot Customer { get; init; } = new();
    public DateTime StartDateUtc { get; init; }
    public DateTime EndDateUtc { get; init; }
    public List<CheckoutTenantPayload> Tenants { get; init; } = new();
    public string? Notes { get; init; }
    public string IdempotencyKey { get; init; } = string.Empty;
    public decimal TotalAmount { get; init; }
    public decimal DepositAmount { get; init; }
}

internal sealed record CheckoutTenantPayload
{
    public Guid TenantId { get; init; }
    public List<CreateRentalItem> Items { get; init; } = new();
    public decimal TotalAmount { get; init; }
    public decimal DepositAmount { get; init; }
}

internal sealed record CheckoutCustomerSnapshot
{
    public Guid CustomerId { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string? Email { get; init; }
    public string? PhoneNumber { get; init; }
    public string? Address { get; init; }
    public string? DocumentNumber { get; init; }
    public string? Notes { get; init; }
}
