using SportRental.Shared.Models;

namespace SportRental.Admin.Payments;

internal record PaymentComputationResult(
    decimal TotalAmount,
    decimal DepositAmount,
    int RentalDays,
    Dictionary<Guid, decimal> ProductPrices,
    Dictionary<Guid, Guid> ProductTenants,
    IReadOnlyList<TenantPaymentBreakdown> Tenants);

internal record TenantPaymentBreakdown(
    Guid TenantId,
    DateTime StartDateUtc,
    DateTime EndDateUtc,
    RentalTypeDto RentalType,
    int? HoursRented,
    int RentalDays,
    decimal TotalAmount,
    decimal DepositAmount,
    IReadOnlyList<ComputedRentalItem> Items);

internal record ComputedRentalItem(
    Guid ProductId,
    int Quantity,
    decimal PricePerDay,
    decimal? PricePerHour,
    decimal Subtotal);
