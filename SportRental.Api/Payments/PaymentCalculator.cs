using Microsoft.EntityFrameworkCore;
using SportRental.Infrastructure.Data;
using SportRental.Shared.Models;

namespace SportRental.Api.Payments;

internal static class PaymentCalculator
{
    public static async Task<PaymentComputationResult> ComputeAsync(
        Guid tenantId,
        PaymentQuoteRequest req,
        ApplicationDbContext db,
        CancellationToken ct,
        bool allowMixedTenants = true)
    {
        ArgumentNullException.ThrowIfNull(req);

        if (req.EndDateUtc <= req.StartDateUtc)
        {
            throw new InvalidOperationException("EndDateUtc must be after StartDateUtc.");
        }

        if (req.Items.Count == 0)
        {
            throw new InvalidOperationException("At least one item is required.");
        }

        var productIds = req.Items.Select(i => i.ProductId).Distinct().ToList();

        var products = await db.Products
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new
            {
                p.Id,
                p.TenantId,
                p.DailyPrice,
                p.HourlyPrice
            })
            .ToListAsync(ct);

        if (products.Count != productIds.Count)
        {
            throw new InvalidOperationException("One or more products are not available.");
        }

        if (!allowMixedTenants && tenantId == Guid.Empty)
        {
            throw new InvalidOperationException("TenantId jest wymagany gdy mieszanie wypozyczalni jest wylaczone.");
        }

        if (!allowMixedTenants && products.Any(p => p.TenantId != tenantId))
        {
            throw new InvalidOperationException("Produkty musza nalezec do wybranej wypozyczalni.");
        }

        var dailyPrices = products.ToDictionary(p => p.Id, p => p.DailyPrice);
        var hourlyPrices = products.ToDictionary(p => p.Id, p => p.HourlyPrice);
        var productTenants = products.ToDictionary(p => p.Id, p => p.TenantId);

        var rentalDays = Math.Max(1, (int)Math.Ceiling((req.EndDateUtc - req.StartDateUtc).TotalDays));
        var isHourly = req.RentalType == RentalTypeDto.Hourly && req.HoursRented.HasValue && req.HoursRented.Value > 0;
        var hours = req.HoursRented ?? 1;
        
        var productPrices = new Dictionary<Guid, decimal>();
        var tenantTotals = new Dictionary<Guid, decimal>();
        var tenantItems = new Dictionary<Guid, Dictionary<Guid, CreateRentalItem>>();
        decimal total = 0m;

        foreach (var item in req.Items)
        {
            var dailyPrice = dailyPrices[item.ProductId];
            var hourlyPrice = hourlyPrices[item.ProductId];
            var tenant = productTenants[item.ProductId];
            
            // Oblicz cenę w zależności od typu wynajmu
            decimal itemTotal;
            if (isHourly && hourlyPrice.HasValue && hourlyPrice.Value > 0)
            {
                itemTotal = hourlyPrice.Value * item.Quantity * hours;
                productPrices[item.ProductId] = hourlyPrice.Value;
            }
            else
            {
                itemTotal = dailyPrice * item.Quantity * rentalDays;
                productPrices[item.ProductId] = dailyPrice;
            }

            if (!tenantItems.TryGetValue(tenant, out var itemsForTenant))
            {
                itemsForTenant = new Dictionary<Guid, CreateRentalItem>();
                tenantItems[tenant] = itemsForTenant;
            }

            if (itemsForTenant.TryGetValue(item.ProductId, out var existingItem))
            {
                existingItem.Quantity += item.Quantity;
            }
            else
            {
                itemsForTenant[item.ProductId] = new CreateRentalItem
                {
                    ProductId = item.ProductId,
                    Quantity = item.Quantity
                };
            }

            total += itemTotal;

            if (!tenantTotals.ContainsKey(tenant))
            {
                tenantTotals[tenant] = 0m;
            }

            tenantTotals[tenant] += itemTotal;
        }

        var deposit = Math.Round(total * 0.3m, 2, MidpointRounding.AwayFromZero);
        var tenantBreakdowns = BuildTenantBreakdown(tenantTotals, tenantItems, deposit);

        return new PaymentComputationResult(
            total,
            deposit,
            rentalDays,
            productPrices,
            productTenants,
            tenantBreakdowns);
    }

    private static IReadOnlyList<TenantPaymentBreakdown> BuildTenantBreakdown(
        IReadOnlyDictionary<Guid, decimal> tenantTotals,
        IReadOnlyDictionary<Guid, Dictionary<Guid, CreateRentalItem>> tenantItems,
        decimal overallDeposit)
    {
        var ordered = tenantTotals.OrderBy(t => t.Key).ToList();
        var result = new List<TenantPaymentBreakdown>(ordered.Count);

        if (ordered.Count == 0)
        {
            return result;
        }

        var total = ordered.Sum(t => t.Value);
        decimal allocated = 0m;

        for (var i = 0; i < ordered.Count; i++)
        {
            var (tenantId, tenantTotal) = ordered[i];
            decimal tenantDeposit;
            if (overallDeposit <= 0 || total <= 0)
            {
                tenantDeposit = 0m;
            }
            else
            {
                tenantDeposit = Math.Round(overallDeposit * (tenantTotal / total), 2, MidpointRounding.AwayFromZero);
                if (i == ordered.Count - 1)
                {
                    tenantDeposit = overallDeposit - allocated;
                }
            }

            allocated += tenantDeposit;

            var items = tenantItems.TryGetValue(tenantId, out var dict)
                ? dict.Values.ToList()
                : new List<CreateRentalItem>();

            result.Add(new TenantPaymentBreakdown(
                tenantId,
                tenantTotal,
                tenantDeposit,
                items));
        }

        return result;
    }
}
