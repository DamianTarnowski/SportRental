using System;
using System.Collections.Generic;
using SportRental.Shared.Models;

namespace SportRental.Api.Payments;

internal record PaymentComputationResult(
    decimal TotalAmount,
    decimal DepositAmount,
    int RentalDays,
    Dictionary<Guid, decimal> ProductPrices,
    Dictionary<Guid, Guid> ProductTenants,
    IReadOnlyList<TenantPaymentBreakdown> Tenants);

internal record TenantPaymentBreakdown(
    Guid TenantId,
    decimal TotalAmount,
    decimal DepositAmount,
    IReadOnlyList<CreateRentalItem> Items);
