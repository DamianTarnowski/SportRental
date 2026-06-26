using Microsoft.EntityFrameworkCore;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;

namespace SportRental.Admin.Services.Dashboard;

public record DashboardMetrics(
    int PendingIssues,
    int PendingReturns,
    int ActiveRentals,
    int AvailableProducts,
    decimal TodayRevenue,
    int TodayPickups,
    int TodayReturns,
    int UnsignedContracts,
    int OverdueReturns,
    int UnpaidRentals,
    decimal DueTodayAmount,
    decimal OverdueAmount,
    decimal PaidTodayAmount,
    decimal MonthRevenue);

public enum TimelineEventType { Pickup, Return, Overdue }

public record TimelineEvent(
    DateTime LocalTime,
    TimelineEventType Type,
    string CustomerName,
    string Summary,
    Guid RentalId);

/// Centralne metryki Dashboardu i Wydań — żeby liczniki zawsze się zgadzały
/// (Maciej: „pulpit pokazuje 0 a tam 12 — jak się uda poprawić, mega").
/// Używają tych samych warunków co odpowiednie listy w EquipmentHandling.razor.
public class DashboardMetricsService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;

    public DashboardMetricsService(IDbContextFactory<ApplicationDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<DashboardMetrics> GetAsync(Guid tenantId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var today = now.Date;
        var tomorrow = today.AddDays(1);

        // PERF: każdy count używa własnego DbContext → możemy puścić równolegle.
        // Jeden kontekst nie obsłuży concurrent queries, więc per-task scope.
        async Task<int> CountRentals(Func<IQueryable<Rental>, IQueryable<Rental>> filter)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            db.SetTenant(tenantId);
            return await filter(db.Rentals.AsNoTracking()).CountAsync(ct);
        }

        async Task<int> CountProducts(Func<IQueryable<Product>, IQueryable<Product>> filter)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            db.SetTenant(tenantId);
            return await filter(db.Products.AsNoTracking()).CountAsync(ct);
        }

        async Task<decimal> SumRevenue()
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            db.SetTenant(tenantId);
            return await db.Rentals.AsNoTracking()
                .Where(r => r.StartDateUtc >= today && r.StartDateUtc < tomorrow
                    && r.Status != RentalStatus.Cancelled)
                .SumAsync(r => (decimal?)r.TotalAmount, ct) ?? 0m;
        }

        async Task<decimal> SumByFilter(Func<IQueryable<Rental>, IQueryable<Rental>> filter)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            db.SetTenant(tenantId);
            return await filter(db.Rentals.AsNoTracking())
                .SumAsync(r => (decimal?)r.TotalAmount, ct) ?? 0m;
        }

        var firstOfMonth = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        // Whitelist statusów uznawanych za "opłacone" (zsynchronizowane z RentalGuards.IsRentalPaid).
        var paidStatuses = new List<string> { "DepositPaid", "succeeded", "paid", "Paid" };

        // 10 równoległych query — typowy czas spada z ~10×50ms = 500ms do ~50-100ms
        var pendingIssuesT = CountRentals(q => q.Where(r =>
            (r.Status == RentalStatus.Confirmed || r.Status == RentalStatus.Pending)
            && r.IssuedAtUtc == null));

        var pendingReturnsT = CountRentals(q => q.Where(r =>
            r.Status == RentalStatus.Active
            && r.IssuedAtUtc != null
            && r.ReturnedAtUtc == null));

        var activeRentalsT = CountRentals(q => q.Where(r =>
            r.Status == RentalStatus.Active
            && r.StartDateUtc <= now
            && r.EndDateUtc >= now));

        var availableProductsT = CountProducts(q => q.Where(p =>
            p.IsActive && p.AvailableQuantity > 0 && !p.IsDeleted));

        var todayRevenueT = SumRevenue();

        var todayPickupsT = CountRentals(q => q.Where(r =>
            r.StartDateUtc >= today && r.StartDateUtc < tomorrow
            && r.IssuedAtUtc == null
            && r.Status != RentalStatus.Cancelled));

        var todayReturnsT = CountRentals(q => q.Where(r =>
            r.EndDateUtc >= today && r.EndDateUtc < tomorrow
            && r.ReturnedAtUtc == null
            && r.Status != RentalStatus.Cancelled));

        var unsignedContractsT = CountRentals(q => q.Where(r =>
            (r.Status == RentalStatus.Confirmed || r.Status == RentalStatus.Pending
                || r.Status == RentalStatus.Active)
            && (r.ContractUrl == null || r.ContractUrl == "")));

        var overdueReturnsT = CountRentals(q => q.Where(r =>
            r.Status == RentalStatus.Active
            && r.EndDateUtc < now
            && r.ReturnedAtUtc == null));

        var unpaidRentalsT = CountRentals(q => q.Where(r =>
            (r.Status == RentalStatus.Confirmed || r.Status == RentalStatus.Active)
            && (r.PaymentStatus == "" || r.PaymentStatus == "pending"
                || r.PaymentStatus == "requires_payment_method"
                || r.PaymentStatus == "failed")));

        // NXRE r2 punkt 2: 4 nowe metryki płatności dla kafelków na dashboardzie.
        // Każda jako osobny await żeby utrzymać czystość WhenAll.
        var dueTodayT = SumByFilter(q => q.Where(r =>
            r.StartDateUtc >= today && r.StartDateUtc < tomorrow
            && r.Status != RentalStatus.Cancelled
            && !paidStatuses.Contains(r.PaymentStatus)));

        var overdueAmountT = SumByFilter(q => q.Where(r =>
            r.Status != RentalStatus.Cancelled && r.Status != RentalStatus.Completed
            && r.StartDateUtc < now
            && !paidStatuses.Contains(r.PaymentStatus)));

        var paidTodayT = SumByFilter(q => q.Where(r =>
            r.PaidAtUtc != null && r.PaidAtUtc >= today && r.PaidAtUtc < tomorrow));

        var monthRevenueT = SumByFilter(q => q.Where(r =>
            r.PaidAtUtc != null && r.PaidAtUtc >= firstOfMonth));

        await Task.WhenAll(
            pendingIssuesT, pendingReturnsT, activeRentalsT, availableProductsT,
            todayPickupsT, todayReturnsT, unsignedContractsT, overdueReturnsT, unpaidRentalsT);
        var todayRevenue = await todayRevenueT;
        await Task.WhenAll(dueTodayT, overdueAmountT, paidTodayT, monthRevenueT);

        return new DashboardMetrics(
            pendingIssuesT.Result, pendingReturnsT.Result, activeRentalsT.Result, availableProductsT.Result,
            todayRevenue, todayPickupsT.Result, todayReturnsT.Result, unsignedContractsT.Result,
            overdueReturnsT.Result, unpaidRentalsT.Result,
            dueTodayT.Result, overdueAmountT.Result, paidTodayT.Result, monthRevenueT.Result);
    }

    /// Oś dnia (Maciej: „9:00 wydanie, 11:00 zwrot, 16:00 odbiór").
    /// Zwraca eventy z dzisiejszego dnia + spóźnione zwroty.
    public async Task<List<TimelineEvent>> GetTodayTimelineAsync(Guid tenantId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.SetTenant(tenantId);

        var now = DateTime.UtcNow;
        var today = now.Date;
        var tomorrow = today.AddDays(1);

        // Wydania dziś (nie wydane jeszcze)
        var pickups = await db.Rentals.AsNoTracking()
            .Include(r => r.Customer)
            .Include(r => r.Items)
            .Where(r => r.StartDateUtc >= today && r.StartDateUtc < tomorrow
                && r.IssuedAtUtc == null
                && r.Status != RentalStatus.Cancelled)
            .Take(50)
            .ToListAsync(ct);

        // Zwroty dziś (nie zwrócone jeszcze)
        var returns = await db.Rentals.AsNoTracking()
            .Include(r => r.Customer)
            .Include(r => r.Items)
            .Where(r => r.EndDateUtc >= today && r.EndDateUtc < tomorrow
                && r.ReturnedAtUtc == null
                && r.Status != RentalStatus.Cancelled)
            .Take(50)
            .ToListAsync(ct);

        // Spóźnione zwroty (overdue z poprzednich dni)
        var overdue = await db.Rentals.AsNoTracking()
            .Include(r => r.Customer)
            .Include(r => r.Items)
            .Where(r => r.Status == RentalStatus.Active
                && r.EndDateUtc < today
                && r.ReturnedAtUtc == null)
            .OrderByDescending(r => r.EndDateUtc)
            .Take(20)
            .ToListAsync(ct);

        var events = new List<TimelineEvent>();
        events.AddRange(pickups.Select(r => new TimelineEvent(
            r.StartDateUtc.ToLocalTime(), TimelineEventType.Pickup,
            r.Customer?.FullName ?? "—",
            $"Wydanie · {r.Items.Sum(i => i.Quantity)} szt.",
            r.Id)));
        events.AddRange(returns.Select(r => new TimelineEvent(
            r.EndDateUtc.ToLocalTime(), TimelineEventType.Return,
            r.Customer?.FullName ?? "—",
            $"Zwrot · {r.Items.Sum(i => i.Quantity)} szt.",
            r.Id)));
        events.AddRange(overdue.Select(r => new TimelineEvent(
            r.EndDateUtc.ToLocalTime(), TimelineEventType.Overdue,
            r.Customer?.FullName ?? "—",
            $"Spóźniony zwrot · {(int)(DateTime.UtcNow - r.EndDateUtc).TotalDays + 1} dni",
            r.Id)));

        return events.OrderBy(e => e.LocalTime).ToList();
    }
}
