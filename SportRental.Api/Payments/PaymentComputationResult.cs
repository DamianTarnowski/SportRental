using System;
using System.Collections.Generic;

namespace SportRental.Api.Payments;

internal record PaymentComputationResult(
    decimal TotalAmount,
    decimal DepositAmount,
    int RentalDays,
    Dictionary<Guid, decimal> ProductPrices);
