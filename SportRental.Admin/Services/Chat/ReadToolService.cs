using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;

namespace SportRental.Admin.Services.Chat;

/// <summary>
/// Read-only tools dla asystenta — pobiera dane domeny aplikacji żeby model mógł
/// odpowiedzieć konkretnymi liczbami zamiast halucynować. WSZYSTKO filtrowane po
/// TenantId z kontekstu — asystent nigdy nie widzi danych innego tenanta, nawet dla
/// SuperAdmin (ten musi przelogować się na właściwy tenant żeby widzieć jego dane).
/// </summary>
public sealed class ReadToolService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
    private readonly ILogger<ReadToolService> _logger;

    public ReadToolService(IDbContextFactory<ApplicationDbContext> dbFactory, ILogger<ReadToolService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    /// <summary>
    /// Wynajmy do wydania dzisiaj (StartDate ≤ tomorrow) i do zwrotu dziś
    /// (EndDate ≤ tomorrow), tylko Active/Confirmed/Issued. Sortowane po EndDate.
    /// </summary>
    public async Task<string> GetTodayRentalsAsync(Guid tenantId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.SetTenant(tenantId);

        var nowUtc = DateTime.UtcNow;
        var tomorrowEodUtc = DateTime.UtcNow.Date.AddDays(2); // bezpieczny próg „w ciągu doby"

        var rentals = await db.Rentals
            .Include(r => r.Customer)
            .Include(r => r.Items)
            .Where(r => r.Status == RentalStatus.Active
                     || r.Status == RentalStatus.Confirmed
                     || r.Status == RentalStatus.Draft)
            .Where(r => r.StartDateUtc <= tomorrowEodUtc && r.EndDateUtc >= nowUtc)
            .OrderBy(r => r.EndDateUtc)
            .Take(20)
            .Select(r => new
            {
                id = r.Id,
                customer = r.Customer != null ? r.Customer.FullName : "(nieznany)",
                customerEmail = r.Customer != null ? r.Customer.Email : null,
                status = r.Status.ToString(),
                startsAt = r.StartDateUtc,
                endsAt = r.EndDateUtc,
                itemsCount = r.Items.Count,
                totalAmount = r.TotalAmount
            })
            .ToListAsync(ct);

        return JsonSerializer.Serialize(new
        {
            asOf = DateTime.UtcNow,
            count = rentals.Count,
            rentals
        });
    }

    /// <summary>
    /// Status produktu po SKU lub nazwie (case-insensitive contains). Pierwsze 5 trafień.
    /// </summary>
    public async Task<string> GetProductStatusAsync(Guid tenantId, string skuOrName, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.SetTenant(tenantId);

        if (string.IsNullOrWhiteSpace(skuOrName))
            return JsonSerializer.Serialize(new { error = "skuOrName required" });

        var query = skuOrName.Trim().ToLowerInvariant();
        var products = await db.Products
            .Where(p => p.Sku.ToLower() == query
                     || p.Sku.ToLower().Contains(query)
                     || p.Name.ToLower().Contains(query))
            .OrderBy(p => p.Sku == query ? 0 : (p.Sku.ToLower().StartsWith(query) ? 1 : 2))
            .Take(5)
            .Select(p => new
            {
                id = p.Id,
                sku = p.Sku,
                name = p.Name,
                available = p.Available && p.IsActive,
                availableQuantity = p.AvailableQuantity,
                dailyPrice = p.DailyPrice,
                hourlyPrice = p.HourlyPrice,
                category = p.Category
            })
            .ToListAsync(ct);

        if (products.Count == 0)
            return JsonSerializer.Serialize(new { count = 0, message = "Brak produktów dla zapytania '" + skuOrName + "'" });

        return JsonSerializer.Serialize(new { count = products.Count, products });
    }

    /// <summary>
    /// Trust info klienta — szuka po email (exact) albo phone albo nazwisku (contains).
    /// </summary>
    public async Task<string> GetCustomerTrustAsync(Guid tenantId, string emailOrPhoneOrName, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.SetTenant(tenantId);

        if (string.IsNullOrWhiteSpace(emailOrPhoneOrName))
            return JsonSerializer.Serialize(new { error = "query required" });

        var q = emailOrPhoneOrName.Trim().ToLowerInvariant();
        var customers = await db.Customers
            .Where(c =>
                (c.Email != null && c.Email.ToLower() == q) ||
                (c.PhoneNumber != null && c.PhoneNumber == emailOrPhoneOrName.Trim()) ||
                c.FullName.ToLower().Contains(q))
            .Take(3)
            .Select(c => new
            {
                id = c.Id,
                fullName = c.FullName,
                email = c.Email,
                phone = c.PhoneNumber,
                trustLevel = c.TrustLevel.ToString(),
                completedRentals = c.TrustCompletedRentalsCount,
                averageScore = c.TrustAverageScore,
                incidentCount = c.TrustIncidentCount,
                hasManualOverride = c.TrustLevelManualOverride.HasValue,
                overrideReason = c.TrustLevelManualReason
            })
            .ToListAsync(ct);

        if (customers.Count == 0)
            return JsonSerializer.Serialize(new { count = 0, message = "Brak klientów dla zapytania '" + emailOrPhoneOrName + "'" });

        return JsonSerializer.Serialize(new { count = customers.Count, customers });
    }

    /// <summary>
    /// Liczba aktywnych wynajmów + krótki breakdown.
    /// </summary>
    public async Task<string> CountActiveRentalsAsync(Guid tenantId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.SetTenant(tenantId);

        var nowUtc = DateTime.UtcNow;
        var tomorrowUtc = DateTime.UtcNow.Date.AddDays(1);
        var weekAheadUtc = DateTime.UtcNow.Date.AddDays(7);

        var stats = await db.Rentals
            .GroupBy(_ => 1)
            .Select(g => new
            {
                active = g.Count(r => r.Status == RentalStatus.Active || r.Status == RentalStatus.Confirmed),
                draft = g.Count(r => r.Status == RentalStatus.Draft),
                completed = g.Count(r => r.Status == RentalStatus.Completed),
                cancelled = g.Count(r => r.Status == RentalStatus.Cancelled),
                endingToday = g.Count(r => r.EndDateUtc >= nowUtc && r.EndDateUtc < tomorrowUtc
                                        && (r.Status == RentalStatus.Active || r.Status == RentalStatus.Confirmed)),
                startingToday = g.Count(r => r.StartDateUtc >= DateTime.UtcNow.Date && r.StartDateUtc < tomorrowUtc),
                upcomingThisWeek = g.Count(r => r.StartDateUtc >= tomorrowUtc && r.StartDateUtc < weekAheadUtc)
            })
            .FirstOrDefaultAsync(ct);

        return JsonSerializer.Serialize(stats ?? new
        {
            active = 0, draft = 0, completed = 0, cancelled = 0,
            endingToday = 0, startingToday = 0, upcomingThisWeek = 0
        });
    }
}
