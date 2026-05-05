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
    /// Wyszukiwanie wynajmów: po fragmencie nazwy/email klienta, statusie i oknie czasowym.
    /// </summary>
    public async Task<string> SearchRentalsAsync(
        Guid tenantId,
        string? customerQuery,
        string? status,
        int? daysAhead,
        int? daysBehind,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.SetTenant(tenantId);

        var q = db.Rentals.Include(r => r.Customer).Include(r => r.Items).AsQueryable();

        if (!string.IsNullOrWhiteSpace(customerQuery))
        {
            var qLower = customerQuery.Trim().ToLowerInvariant();
            q = q.Where(r => r.Customer != null &&
                (r.Customer.FullName.ToLower().Contains(qLower) ||
                 (r.Customer.Email != null && r.Customer.Email.ToLower().Contains(qLower)) ||
                 (r.Customer.PhoneNumber != null && r.Customer.PhoneNumber.Contains(customerQuery.Trim()))));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (Enum.TryParse<RentalStatus>(status, ignoreCase: true, out var st))
                q = q.Where(r => r.Status == st);
        }

        var nowUtc = DateTime.UtcNow;
        if (daysAhead.HasValue && daysAhead.Value > 0)
        {
            var threshold = nowUtc.AddDays(daysAhead.Value);
            q = q.Where(r => r.StartDateUtc <= threshold);
        }
        if (daysBehind.HasValue && daysBehind.Value > 0)
        {
            var threshold = nowUtc.AddDays(-daysBehind.Value);
            q = q.Where(r => r.EndDateUtc >= threshold);
        }

        var rentals = await q
            .OrderByDescending(r => r.StartDateUtc)
            .Take(15)
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

        return JsonSerializer.Serialize(new { count = rentals.Count, rentals });
    }

    /// <summary>Wynajmy zaległe — EndDate &lt; teraz, status nie Completed/Cancelled.</summary>
    public async Task<string> GetOverdueRentalsAsync(Guid tenantId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.SetTenant(tenantId);

        var nowUtc = DateTime.UtcNow;
        var raw = await db.Rentals
            .Include(r => r.Customer)
            .Where(r => r.EndDateUtc < nowUtc
                     && r.Status != RentalStatus.Completed
                     && r.Status != RentalStatus.Cancelled)
            .OrderBy(r => r.EndDateUtc)
            .Take(20)
            .Select(r => new
            {
                id = r.Id,
                customer = r.Customer != null ? r.Customer.FullName : "(nieznany)",
                customerEmail = r.Customer != null ? r.Customer.Email : null,
                customerPhone = r.Customer != null ? r.Customer.PhoneNumber : null,
                status = r.Status.ToString(),
                endsAt = r.EndDateUtc,
                totalAmount = r.TotalAmount
            })
            .ToListAsync(ct);

        // daysOverdue liczone w C# — Postgres nie ma DateDiffDay, w EF6+/Npgsql trzeba lokalnie.
        var rentals = raw.Select(r => new
        {
            r.id, r.customer, r.customerEmail, r.customerPhone, r.status, r.endsAt,
            daysOverdue = (int)(nowUtc - r.endsAt).TotalDays,
            r.totalAmount
        }).ToList();

        return JsonSerializer.Serialize(new { count = rentals.Count, rentals });
    }

    /// <summary>
    /// Co użytkownik powinien zrobić DZIŚ: do wydania (Confirmed/Active starting today),
    /// do zwrotu (kończące dziś), niepotwierdzone SMSy, draft do dokończenia.
    /// </summary>
    public async Task<string> GetPendingActionsAsync(Guid tenantId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.SetTenant(tenantId);

        var nowUtc = DateTime.UtcNow;
        var todayStartUtc = DateTime.UtcNow.Date;
        var tomorrowStartUtc = todayStartUtc.AddDays(1);

        var stats = await db.Rentals
            .GroupBy(_ => 1)
            .Select(g => new
            {
                toIssueToday = g.Count(r => (r.Status == RentalStatus.Confirmed || r.Status == RentalStatus.Active)
                                          && r.StartDateUtc >= todayStartUtc && r.StartDateUtc < tomorrowStartUtc
                                          && r.IssuedAtUtc == null),
                toReturnToday = g.Count(r => (r.Status == RentalStatus.Active || r.Status == RentalStatus.Confirmed)
                                          && r.EndDateUtc >= todayStartUtc && r.EndDateUtc < tomorrowStartUtc),
                drafts = g.Count(r => r.Status == RentalStatus.Draft),
                pendingSmsConfirmation = g.Count(r => r.Status == RentalStatus.Confirmed
                                                   && r.IsSmsConfirmationSent && !r.IsSmsConfirmed),
                overdue = g.Count(r => r.EndDateUtc < nowUtc
                                    && r.Status != RentalStatus.Completed
                                    && r.Status != RentalStatus.Cancelled)
            })
            .FirstOrDefaultAsync(ct);

        return JsonSerializer.Serialize(stats ?? new
        {
            toIssueToday = 0, toReturnToday = 0, drafts = 0, pendingSmsConfirmation = 0, overdue = 0
        });
    }

    /// <summary>Przychody za period: today / week / month / year.</summary>
    public async Task<string> GetRevenueSummaryAsync(Guid tenantId, string? period, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.SetTenant(tenantId);

        var nowUtc = DateTime.UtcNow;
        var periodLower = (period ?? "month").Trim().ToLowerInvariant();
        var (label, since) = periodLower switch
        {
            "today" or "dzisiaj" => ("dzisiaj", nowUtc.Date),
            "week" or "tydzien" or "tydzień" => ("tydzień", nowUtc.Date.AddDays(-7)),
            "year" or "rok" => ("rok", nowUtc.Date.AddDays(-365)),
            _ => ("miesiąc", nowUtc.Date.AddDays(-30))
        };

        var revenue = await db.Rentals
            .Where(r => r.CreatedAtUtc >= since
                     && r.Status != RentalStatus.Cancelled
                     && r.Status != RentalStatus.Draft)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                period = label,
                since,
                rentalsCount = g.Count(),
                totalAmount = g.Sum(r => r.TotalAmount),
                completedCount = g.Count(r => r.Status == RentalStatus.Completed),
                completedAmount = g.Where(r => r.Status == RentalStatus.Completed).Sum(r => r.TotalAmount),
                averageAmount = g.Average(r => (decimal?)r.TotalAmount) ?? 0m
            })
            .FirstOrDefaultAsync(ct);

        return JsonSerializer.Serialize(revenue ?? new
        {
            period = label,
            since,
            rentalsCount = 0,
            totalAmount = 0m,
            completedCount = 0,
            completedAmount = 0m,
            averageAmount = 0m
        });
    }

    /// <summary>
    /// Pełna historia klienta — wszystkie wynajmy + trust info. Akceptuje email/phone/imię.
    /// </summary>
    public async Task<string> GetCustomerHistoryAsync(Guid tenantId, string emailOrPhoneOrName, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.SetTenant(tenantId);

        if (string.IsNullOrWhiteSpace(emailOrPhoneOrName))
            return JsonSerializer.Serialize(new { error = "query required" });

        var qLower = emailOrPhoneOrName.Trim().ToLowerInvariant();
        var customer = await db.Customers
            .Where(c =>
                (c.Email != null && c.Email.ToLower() == qLower) ||
                (c.PhoneNumber != null && c.PhoneNumber == emailOrPhoneOrName.Trim()) ||
                c.FullName.ToLower().Contains(qLower))
            .FirstOrDefaultAsync(ct);

        if (customer is null)
            return JsonSerializer.Serialize(new { found = false, query = emailOrPhoneOrName });

        var rentals = await db.Rentals
            .Where(r => r.CustomerId == customer.Id)
            .OrderByDescending(r => r.StartDateUtc)
            .Take(15)
            .Select(r => new
            {
                id = r.Id,
                status = r.Status.ToString(),
                startsAt = r.StartDateUtc,
                endsAt = r.EndDateUtc,
                totalAmount = r.TotalAmount
            })
            .ToListAsync(ct);

        return JsonSerializer.Serialize(new
        {
            found = true,
            customer = new
            {
                id = customer.Id,
                fullName = customer.FullName,
                email = customer.Email,
                phone = customer.PhoneNumber,
                trustLevel = customer.TrustLevel.ToString(),
                completedRentals = customer.TrustCompletedRentalsCount,
                averageScore = customer.TrustAverageScore,
                incidentCount = customer.TrustIncidentCount
            },
            rentalsCount = rentals.Count,
            rentals
        });
    }

    /// <summary>
    /// Najlepsi klienci według liczby wynajmów albo łącznej wartości. Max N (default 10).
    /// </summary>
    public async Task<string> GetTopCustomersAsync(Guid tenantId, string? by, int limit, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.SetTenant(tenantId);

        var byLower = (by ?? "rentals").Trim().ToLowerInvariant();
        var actualLimit = Math.Clamp(limit <= 0 ? 10 : limit, 1, 50);

        var query = db.Rentals
            .Where(r => r.Status != RentalStatus.Cancelled && r.Status != RentalStatus.Draft)
            .GroupBy(r => r.CustomerId)
            .Select(g => new
            {
                customerId = g.Key,
                rentalsCount = g.Count(),
                totalAmount = g.Sum(r => r.TotalAmount),
                lastRentalAt = g.Max(r => r.StartDateUtc)
            });

        query = byLower switch
        {
            "revenue" or "amount" or "wartosc" or "wartość" => query.OrderByDescending(x => x.totalAmount),
            _ => query.OrderByDescending(x => x.rentalsCount)
        };

        var topAggregates = await query.Take(actualLimit).ToListAsync(ct);
        if (topAggregates.Count == 0)
            return JsonSerializer.Serialize(new { count = 0, by = byLower, customers = Array.Empty<object>() });

        var customerIds = topAggregates.Select(x => x.customerId).ToList();
        var customers = await db.Customers
            .Where(c => customerIds.Contains(c.Id))
            .Select(c => new { c.Id, c.FullName, c.Email, c.PhoneNumber, c.TrustLevel })
            .ToListAsync(ct);

        var result = topAggregates
            .Select(t => new
            {
                t.customerId,
                customer = customers.FirstOrDefault(c => c.Id == t.customerId)?.FullName ?? "(nieznany)",
                email = customers.FirstOrDefault(c => c.Id == t.customerId)?.Email,
                phone = customers.FirstOrDefault(c => c.Id == t.customerId)?.PhoneNumber,
                trustLevel = customers.FirstOrDefault(c => c.Id == t.customerId)?.TrustLevel.ToString(),
                rentalsCount = t.rentalsCount,
                totalAmount = t.totalAmount,
                lastRentalAt = t.lastRentalAt
            })
            .ToList();

        return JsonSerializer.Serialize(new { count = result.Count, by = byLower, customers = result });
    }

    /// <summary>Lista pracowników wypożyczalni (wraz z rolą).</summary>
    public async Task<string> GetEmployeeListAsync(Guid tenantId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.SetTenant(tenantId);

        var employees = await db.Employees
            .Where(e => !e.IsDeleted)
            .OrderBy(e => e.FullName)
            .Select(e => new
            {
                id = e.Id,
                fullName = e.FullName,
                email = e.Email,
                phone = e.Telephone,
                role = e.Role.ToString(),
                position = e.Position,
                city = e.City
            })
            .ToListAsync(ct);

        var pendingInvitations = await db.EmployeeInvitations
            .IgnoreQueryFilters()
            .Where(i => i.TenantId == tenantId && !i.IsUsed && i.ExpiresAtUtc > DateTime.UtcNow)
            .Select(i => new { i.Email, role = i.Role.ToString(), expiresAt = i.ExpiresAtUtc })
            .ToListAsync(ct);

        return JsonSerializer.Serialize(new
        {
            employeesCount = employees.Count,
            pendingInvitationsCount = pendingInvitations.Count,
            employees,
            pendingInvitations
        });
    }

    /// <summary>
    /// Forecast prosty — porównuje liczbę wynajmów ostatnich 30 dni vs poprzednich 30 dni
    /// i wyciąga linear extrapolation na następne 30 dni. Opcjonalnie filter po nazwie produktu.
    /// </summary>
    public async Task<string> ForecastDemandAsync(Guid tenantId, string? productQuery, int days, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.SetTenant(tenantId);

        var window = days <= 0 ? 30 : Math.Min(days, 90);
        var now = DateTime.UtcNow;
        var sinceA = now.AddDays(-window);          // ostatnie N dni
        var sinceB = now.AddDays(-window * 2);     // poprzednie N dni

        var baseQuery = db.Rentals
            .Where(r => r.Status != RentalStatus.Cancelled && r.Status != RentalStatus.Draft);

        // Filter po produkcie (po nazwie/SKU contains)
        if (!string.IsNullOrWhiteSpace(productQuery))
        {
            var q = productQuery.Trim().ToLowerInvariant();
            baseQuery = baseQuery.Where(r => r.Items.Any(i =>
                db.Products.Any(p => p.Id == i.ProductId
                    && (p.Name.ToLower().Contains(q) || p.Sku.ToLower().Contains(q)))));
        }

        var recent = await baseQuery.Where(r => r.StartDateUtc >= sinceA).CountAsync(ct);
        var previous = await baseQuery.Where(r => r.StartDateUtc >= sinceB && r.StartDateUtc < sinceA).CountAsync(ct);

        var trendPct = previous == 0 ? (recent > 0 ? 100.0 : 0.0)
                                     : Math.Round((recent - previous) * 100.0 / previous, 1);
        var projection = previous == 0 ? recent : (int)Math.Round(recent * (1 + trendPct / 100.0));

        return JsonSerializer.Serialize(new
        {
            windowDays = window,
            productFilter = productQuery,
            rentalsLastWindow = recent,
            rentalsPreviousWindow = previous,
            trendPct,
            projectedNextWindow = projection,
            interpretation = trendPct >= 25 ? "rosnące zainteresowanie"
                : trendPct <= -25 ? "spadek zainteresowania"
                : "stabilnie"
        });
    }

    /// <summary>Znajdź wynajem powiązany z konkretnym sprzętem (po SKU produktu).</summary>
    public async Task<string> FindRentalBySkuAsync(Guid tenantId, string sku, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.SetTenant(tenantId);

        if (string.IsNullOrWhiteSpace(sku))
            return JsonSerializer.Serialize(new { error = "sku required" });

        var product = await db.Products.FirstOrDefaultAsync(
            p => p.Sku.ToLower() == sku.Trim().ToLowerInvariant(), ct);
        if (product is null)
            return JsonSerializer.Serialize(new { found = false, message = "Brak produktu o SKU '" + sku + "'" });

        var rentals = await db.RentalItems
            .Include(ri => ri.Rental).ThenInclude(r => r!.Customer)
            .Where(ri => ri.ProductId == product.Id
                     && ri.Rental != null
                     && ri.Rental.Status != RentalStatus.Completed
                     && ri.Rental.Status != RentalStatus.Cancelled)
            .OrderByDescending(ri => ri.Rental!.StartDateUtc)
            .Take(5)
            .Select(ri => new
            {
                rentalId = ri.RentalId,
                customer = ri.Rental!.Customer != null ? ri.Rental.Customer.FullName : "(nieznany)",
                customerPhone = ri.Rental.Customer != null ? ri.Rental.Customer.PhoneNumber : null,
                status = ri.Rental.Status.ToString(),
                startsAt = ri.Rental.StartDateUtc,
                endsAt = ri.Rental.EndDateUtc,
                quantity = ri.Quantity
            })
            .ToListAsync(ct);

        return JsonSerializer.Serialize(new
        {
            found = true,
            product = new { id = product.Id, sku = product.Sku, name = product.Name },
            activeRentalsCount = rentals.Count,
            activeRentals = rentals
        });
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
