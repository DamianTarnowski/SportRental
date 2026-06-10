using Microsoft.EntityFrameworkCore;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;

namespace SportRental.Admin.Services;

/// <summary>
/// Faza 8a — godziny pracy wypożyczalni. Blokuje rezerwacje poza godzinami
/// chyba że admin override.
/// </summary>
public interface IBusinessHoursService
{
    Task<bool> IsOpenAtAsync(Guid tenantId, DateTime utc, CancellationToken ct = default);
    Task<BusinessHoursValidationResult> ValidateRentalWindowAsync(
        Guid tenantId, DateTime startUtc, DateTime endUtc, CancellationToken ct = default);
    Task<BusinessHoursSchedule> GetOrCreateScheduleAsync(Guid tenantId, CancellationToken ct = default);
    Task SaveScheduleAsync(Guid tenantId, IEnumerable<BusinessHoursDay> days, CancellationToken ct = default);
}

public sealed record BusinessHoursValidationResult(bool IsValid, string? Reason);

public sealed class BusinessHoursService : IBusinessHoursService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
    private readonly ILogger<BusinessHoursService> _logger;

    /// <summary>Strefa lokalna wypożyczalni — Europe/Warsaw. W przyszłości można dodać per-tenant.</summary>
    private static readonly TimeZoneInfo LocalTz = ResolveWarsaw();

    public BusinessHoursService(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        ILogger<BusinessHoursService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task<bool> IsOpenAtAsync(Guid tenantId, DateTime utc, CancellationToken ct = default)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), LocalTz);
        var date = DateOnly.FromDateTime(local);
        var time = TimeOnly.FromDateTime(local);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.SetTenant(tenantId);

        // 1) exception dla tej konkretnej daty wygrywa
        var ex = await db.BusinessHoursExceptions
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Date == date, ct);
        if (ex != null)
        {
            if (ex.IsClosed) return false;
            if (ex.CustomOpen.HasValue && ex.CustomClose.HasValue)
                return time >= ex.CustomOpen.Value && time < ex.CustomClose.Value;
            // exception bez customOpen = traktuj jak otwarte cały dzień
            return true;
        }

        // 2) standardowy schedule per dzień tygodnia
        var dow = local.DayOfWeek;
        var schedule = await db.BusinessHoursSchedules
            .AsNoTracking()
            .Include(s => s.Days)
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);

        // Default fallback gdy brak własnego schedule — 8:00-20:00 codziennie
        if (schedule == null || schedule.Days.Count == 0)
        {
            return time >= new TimeOnly(8, 0) && time < new TimeOnly(20, 0);
        }

        var day = schedule.Days.FirstOrDefault(d => d.DayOfWeek == dow);
        if (day == null || day.IsClosed) return false;
        if (!day.OpenFrom.HasValue || !day.OpenTo.HasValue) return false;

        return time >= day.OpenFrom.Value && time < day.OpenTo.Value;
    }

    public async Task<BusinessHoursValidationResult> ValidateRentalWindowAsync(
        Guid tenantId, DateTime startUtc, DateTime endUtc, CancellationToken ct = default)
    {
        if (!await IsOpenAtAsync(tenantId, startUtc, ct))
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(startUtc, DateTimeKind.Utc), LocalTz);
            return new BusinessHoursValidationResult(false,
                $"Wypożyczalnia jest zamknięta o godzinie odbioru ({local:dd.MM.yyyy HH:mm}).");
        }

        if (!await IsOpenAtAsync(tenantId, endUtc, ct))
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(endUtc, DateTimeKind.Utc), LocalTz);
            return new BusinessHoursValidationResult(false,
                $"Wypożyczalnia jest zamknięta o godzinie zwrotu ({local:dd.MM.yyyy HH:mm}).");
        }

        return new BusinessHoursValidationResult(true, null);
    }

    public async Task<BusinessHoursSchedule> GetOrCreateScheduleAsync(Guid tenantId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.SetTenant(tenantId);

        var schedule = await db.BusinessHoursSchedules
            .Include(s => s.Days)
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);

        if (schedule != null) return schedule;

        // Stwórz default 8:00–20:00 wszystkie dni
        schedule = new BusinessHoursSchedule
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            Days = Enum.GetValues<DayOfWeek>().Select(dow => new BusinessHoursDay
            {
                Id = Guid.NewGuid(),
                DayOfWeek = dow,
                IsClosed = false,
                OpenFrom = new TimeOnly(8, 0),
                OpenTo = new TimeOnly(20, 0)
            }).ToList()
        };

        db.BusinessHoursSchedules.Add(schedule);
        await db.SaveChangesAsync(ct);
        _logger.LogInformation("Created default 8-20 schedule for tenant {TenantId}", tenantId);
        return schedule;
    }

    public async Task SaveScheduleAsync(Guid tenantId, IEnumerable<BusinessHoursDay> days, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.SetTenant(tenantId);

        var schedule = await db.BusinessHoursSchedules
            .Include(s => s.Days)
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);

        if (schedule == null)
        {
            schedule = new BusinessHoursSchedule
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                CreatedAtUtc = DateTime.UtcNow
            };
            db.BusinessHoursSchedules.Add(schedule);
        }

        // Replace days completely (cascade delete usuwa stare przy SaveChanges)
        db.BusinessHoursDays.RemoveRange(schedule.Days);
        schedule.Days = days.Select(d => new BusinessHoursDay
        {
            Id = Guid.NewGuid(),
            ScheduleId = schedule.Id,
            DayOfWeek = d.DayOfWeek,
            IsClosed = d.IsClosed,
            OpenFrom = d.IsClosed ? null : d.OpenFrom,
            OpenTo = d.IsClosed ? null : d.OpenTo
        }).ToList();
        schedule.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        _logger.LogInformation("Saved business hours for tenant {TenantId}: {DayCount} days", tenantId, schedule.Days.Count);
    }

    private static TimeZoneInfo ResolveWarsaw()
    {
        // .NET 8+ obsługuje IANA na Windows i Linux. Bezpieczne fallback.
        try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw"); }
        catch
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time"); }
            catch { return TimeZoneInfo.Utc; }
        }
    }
}
