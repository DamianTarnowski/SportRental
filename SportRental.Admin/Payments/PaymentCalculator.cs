using Microsoft.EntityFrameworkCore;
using SportRental.Admin.Services.Time;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;
using SportRental.Shared.Models;
using SportRental.Shared.Time;

namespace SportRental.Admin.Payments;

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

        if (req.RentalGroups is { Count: > 0 })
            return await ComputeGroupedAsync(req.RentalGroups, db, ct);

        return await ComputeFlatAsync(tenantId, req, db, ct, allowMixedTenants);
    }

    private static async Task<PaymentComputationResult> ComputeGroupedAsync(
        IReadOnlyList<RentalGroupQuoteRequest> groups,
        ApplicationDbContext db,
        CancellationToken ct)
    {
        if (groups.Count == 0)
            throw new InvalidOperationException("Koszyk jest pusty.");
        if (groups.Count > 10)
            throw new InvalidOperationException("Jedno zamówienie może obejmować maksymalnie 10 wypożyczalni.");
        if (groups.Any(group => group.TenantId == Guid.Empty))
            throw new InvalidOperationException("Każda grupa musi wskazywać wypożyczalnię.");
        if (groups.GroupBy(group => group.TenantId).Any(group => group.Count() > 1))
            throw new InvalidOperationException("Jedna wypożyczalnia może wystąpić tylko raz w zamówieniu.");

        var allProductIds = groups.SelectMany(group => group.Items).Select(item => item.ProductId).ToList();
        if (allProductIds.Count == 0)
            throw new InvalidOperationException("Koszyk jest pusty.");
        if (allProductIds.Count > 50)
            throw new InvalidOperationException("Jedno zamówienie może zawierać maksymalnie 50 produktów.");
        if (allProductIds.Distinct().Count() != allProductIds.Count)
            throw new InvalidOperationException("Produkt może należeć tylko do jednej grupy rezerwacji.");

        var results = new List<PaymentComputationResult>(groups.Count);
        foreach (var group in groups)
        {
            var flatRequest = new PaymentQuoteRequest
            {
                StartDateUtc = group.StartDateUtc,
                EndDateUtc = group.EndDateUtc,
                Items = group.Items,
                RentalType = group.RentalType,
                HoursRented = group.HoursRented
            };
            results.Add(await ComputeFlatAsync(
                group.TenantId,
                flatRequest,
                db,
                ct,
                allowMixedTenants: false));
        }

        return new PaymentComputationResult(
            results.Sum(result => result.TotalAmount),
            results.Sum(result => result.DepositAmount),
            results.Max(result => result.RentalDays),
            results.SelectMany(result => result.ProductPrices).ToDictionary(pair => pair.Key, pair => pair.Value),
            results.SelectMany(result => result.ProductTenants).ToDictionary(pair => pair.Key, pair => pair.Value),
            results.SelectMany(result => result.Tenants).OrderBy(group => group.TenantId).ToList());
    }

    private static async Task<PaymentComputationResult> ComputeFlatAsync(
        Guid tenantId,
        PaymentQuoteRequest req,
        ApplicationDbContext db,
        CancellationToken ct,
        bool allowMixedTenants)
    {

        if (!PolishRentalTime.IsStartSafelyInFuture(req.StartDateUtc, DateTime.UtcNow))
            throw new InvalidOperationException("Data rozpoczęcia musi być co najmniej 2 minuty w przyszłości.");
        if (req.EndDateUtc <= req.StartDateUtc)
            throw new InvalidOperationException("Data zakończenia musi być późniejsza od rozpoczęcia.");

        var duration = req.EndDateUtc - req.StartDateUtc;
        if (duration > TimeSpan.FromDays(365))
            throw new InvalidOperationException("Maksymalny okres wynajmu to 365 dni.");
        if (req.Items.Count == 0)
            throw new InvalidOperationException("Koszyk jest pusty.");
        if (req.Items.Any(i => i.Quantity <= 0))
            throw new InvalidOperationException("Ilość każdego produktu musi być większa od zera.");
        if (req.Items.GroupBy(i => i.ProductId).Any(g => g.Count() > 1))
            throw new InvalidOperationException("Koszyk zawiera zduplikowane produkty.");

        var isHourly = req.RentalType == RentalTypeDto.Hourly;
        if (isHourly && req.HoursRented is not (>= 1 and <= 24))
            throw new InvalidOperationException("Wynajem godzinowy musi trwać od 1 do 24 godzin.");
        if (isHourly)
        {
            var billedHours = (int)Math.Ceiling(duration.TotalHours);
            if (duration > TimeSpan.FromHours(24) || billedHours != req.HoursRented)
            {
                throw new InvalidOperationException(
                    "Liczba godzin wynajmu nie odpowiada wybranemu terminowi.");
            }
        }

        var productIds = req.Items.Select(i => i.ProductId).ToList();
        var products = await db.Products
            .IgnoreQueryFilters()
            .Where(p => productIds.Contains(p.Id) && p.IsActive && p.Available && !p.Disabled && !p.IsDeleted &&
                        p.AvailableQuantity > 0 &&
                        db.Tenants.IgnoreQueryFilters().Any(t => t.Id == p.TenantId && !t.IsDemo))
            .Select(p => new
            {
                p.Id,
                p.TenantId,
                p.DailyPrice,
                p.HourlyPrice,
                p.AvailableQuantity
            })
            .ToListAsync(ct);

        if (products.Count != productIds.Count)
            throw new InvalidOperationException("Co najmniej jeden produkt jest nieaktywny lub niedostępny.");

        if (!allowMixedTenants && tenantId == Guid.Empty)
            throw new InvalidOperationException("TenantId jest wymagany, gdy mieszanie wypożyczalni jest wyłączone.");
        if (!allowMixedTenants && products.Any(p => p.TenantId != tenantId))
            throw new InvalidOperationException("Produkty muszą należeć do wybranej wypożyczalni.");
        if (isHourly && products.Any(p => !p.HourlyPrice.HasValue || p.HourlyPrice <= 0))
            throw new InvalidOperationException("Co najmniej jeden produkt nie jest dostępny w wynajmie godzinowym.");

        foreach (var item in req.Items)
        {
            var product = products.Single(p => p.Id == item.ProductId);
            if (item.Quantity > product.AvailableQuantity)
                throw new InvalidOperationException("Wybrana ilość przekracza stan magazynowy.");
        }

        // Daily rentals and DateOnly price rules are defined in the shop's local
        // calendar.  Counting the UTC interval directly overcharges a rental that
        // spans the autumn DST change (09:00 -> 09:00 is 25 elapsed UTC hours, but
        // still one rental day in Europe/Warsaw).
        var startLocal = PolishTimeZone.FromUtc(req.StartDateUtc);
        var endLocal = PolishTimeZone.FromUtc(req.EndDateUtc);
        var rentalDays = Math.Max(1, (int)Math.Ceiling((endLocal - startLocal).TotalDays));
        var firstRentalDay = DateOnly.FromDateTime(startLocal);
        var rules = isHourly
            ? new List<PriceRule>()
            : await LoadApplicableRulesAsync(db, productIds, firstRentalDay, rentalDays, ct);

        var computedItems = new List<(Guid TenantId, ComputedRentalItem Item)>();
        foreach (var requestItem in req.Items)
        {
            var product = products.Single(p => p.Id == requestItem.ProductId);
            decimal subtotal;
            if (isHourly)
            {
                subtotal = product.HourlyPrice!.Value * requestItem.Quantity * req.HoursRented!.Value;
            }
            else
            {
                subtotal = 0m;
                for (var dayOffset = 0; dayOffset < rentalDays; dayOffset++)
                {
                    var day = firstRentalDay.AddDays(dayOffset);
                    var dayRule = rules
                        .Where(r => r.ProductId == product.Id && r.FromDate <= day && r.ToDate >= day)
                        .OrderByDescending(r => r.Priority)
                        .ThenByDescending(r => r.CreatedAtUtc)
                        .FirstOrDefault();
                    subtotal += ResolveDailyPrice(product.DailyPrice, dayRule) * requestItem.Quantity;
                }
            }

            computedItems.Add((product.TenantId, new ComputedRentalItem(
                product.Id,
                requestItem.Quantity,
                product.DailyPrice,
                product.HourlyPrice,
                Math.Round(subtotal, 2, MidpointRounding.AwayFromZero))));
        }

        var total = computedItems.Sum(x => x.Item.Subtotal);
        if (total <= 0)
            throw new InvalidOperationException("Wartość zamówienia musi być większa od zera.");

        var deposit = Math.Round(total * 0.3m, 2, MidpointRounding.AwayFromZero);
        var tenantGroups = computedItems
            .GroupBy(x => x.TenantId)
            .OrderBy(g => g.Key)
            .ToList();
        var tenantBreakdowns = new List<TenantPaymentBreakdown>(tenantGroups.Count);
        decimal allocatedDeposit = 0m;

        for (var index = 0; index < tenantGroups.Count; index++)
        {
            var group = tenantGroups[index];
            var tenantTotal = group.Sum(x => x.Item.Subtotal);
            var tenantDeposit = index == tenantGroups.Count - 1
                ? deposit - allocatedDeposit
                : Math.Round(deposit * (tenantTotal / total), 2, MidpointRounding.AwayFromZero);
            allocatedDeposit += tenantDeposit;
            tenantBreakdowns.Add(new TenantPaymentBreakdown(
                group.Key,
                req.StartDateUtc,
                req.EndDateUtc,
                req.RentalType,
                req.HoursRented,
                rentalDays,
                tenantTotal,
                tenantDeposit,
                group.Select(x => x.Item).ToList()));
        }

        return new PaymentComputationResult(
            total,
            deposit,
            rentalDays,
            products.ToDictionary(p => p.Id, p => p.DailyPrice),
            products.ToDictionary(p => p.Id, p => p.TenantId),
            tenantBreakdowns);
    }

    private static async Task<List<PriceRule>> LoadApplicableRulesAsync(
        ApplicationDbContext db,
        List<Guid> productIds,
        DateOnly firstDay,
        int rentalDays,
        CancellationToken ct)
    {
        var lastDay = firstDay.AddDays(rentalDays - 1);
        return await db.PriceRules
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(r => productIds.Contains(r.ProductId) && r.IsActive &&
                        r.FromDate <= lastDay && r.ToDate >= firstDay)
            .ToListAsync(ct);
    }

    private static decimal ResolveDailyPrice(decimal basePrice, PriceRule? rule)
    {
        if (rule is null)
            return basePrice;

        var resolved = rule.Type switch
        {
            PriceRuleType.Multiplier => basePrice * rule.Value,
            PriceRuleType.FixedAdd => basePrice + rule.Value,
            PriceRuleType.FixedReplace => rule.Value,
            _ => basePrice
        };
        return Math.Max(0m, Math.Round(resolved, 2, MidpointRounding.AwayFromZero));
    }
}
