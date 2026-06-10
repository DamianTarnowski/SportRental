using Microsoft.EntityFrameworkCore;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;

namespace SportRental.Admin.Services;

/// <summary>
/// Faza 8b — ceny sezonowe. Wybiera najwyższy priorytet AKTYWNY PriceRule per dzień,
/// fallback na Product.DailyPrice gdy brak rule. Liczy totalna kwotę per rental.
/// </summary>
public interface IPriceCalculator
{
    /// <summary>Cena dzienna produktu w konkretnym dniu (po zastosowaniu rule sezonowych).</summary>
    Task<decimal> CalculateDailyPriceAsync(Guid productId, DateOnly day, CancellationToken ct = default);

    /// <summary>Suma dni × cena (z uwzględnieniem zmiennych cen w okresie).</summary>
    Task<decimal> CalculateRentalTotalAsync(Guid productId, int quantity, DateTime startUtc, DateTime endUtc, CancellationToken ct = default);
}

public sealed class PriceCalculator : IPriceCalculator
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;

    public PriceCalculator(IDbContextFactory<ApplicationDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<decimal> CalculateDailyPriceAsync(Guid productId, DateOnly day, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var product = await db.Products
            .IgnoreQueryFilters()
            .Where(p => p.Id == productId)
            .Select(p => new { p.DailyPrice, p.TenantId })
            .FirstOrDefaultAsync(ct);
        if (product == null) return 0m;

        var rule = await db.PriceRules
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(r => r.ProductId == productId && r.IsActive
                       && r.FromDate <= day && r.ToDate >= day)
            .OrderByDescending(r => r.Priority)
            .ThenByDescending(r => r.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

        if (rule == null) return product.DailyPrice;

        return rule.Type switch
        {
            PriceRuleType.Multiplier => Math.Round(product.DailyPrice * rule.Value, 2),
            PriceRuleType.FixedAdd => Math.Round(product.DailyPrice + rule.Value, 2),
            PriceRuleType.FixedReplace => Math.Round(rule.Value, 2),
            _ => product.DailyPrice
        };
    }

    public async Task<decimal> CalculateRentalTotalAsync(Guid productId, int quantity, DateTime startUtc, DateTime endUtc, CancellationToken ct = default)
    {
        if (startUtc >= endUtc) return 0m;
        if (quantity <= 0) return 0m;

        // Iterujemy dzień po dniu od startUtc (data lokalna Warsaw) do endUtc.
        // Min 1 dzień (jeśli okres < 24h ale > 0).
        var start = startUtc.Date;
        var end = endUtc.Date;
        if (end == start) end = end.AddDays(1); // minimum 1 dzień rozliczeniowy

        decimal total = 0m;
        for (var d = start; d < end; d = d.AddDays(1))
        {
            var dayPrice = await CalculateDailyPriceAsync(productId, DateOnly.FromDateTime(d), ct);
            total += dayPrice;
        }

        return Math.Round(total * quantity, 2);
    }
}
