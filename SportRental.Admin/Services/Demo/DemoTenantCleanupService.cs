using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;

namespace SportRental.Admin.Services.Demo;

/// Background service — co `Interval` (default 6h) usuwa demo tenants którym wygasło `DemoExpiresAtUtc`.
/// Kaskaduje: tenant + user + jego dane (rentals/products/customers/reviews/companyinfo) via FK ON DELETE CASCADE
/// gdzie skonfigurowane, lub manualnie tu.
public class DemoTenantCleanupService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    private readonly IServiceProvider _services;
    private readonly ILogger<DemoTenantCleanupService> _logger;

    public DemoTenantCleanupService(IServiceProvider services, ILogger<DemoTenantCleanupService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Pierwsze sprawdzenie po 5 min od startu — żeby nie blokować boot.
        await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupExpiredDemosAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DemoCleanup failed");
            }
            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task CleanupExpiredDemosAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;

        var expired = await db.Tenants
            .IgnoreQueryFilters()
            .Where(t => t.IsDemo && t.DemoExpiresAtUtc != null && t.DemoExpiresAtUtc < now)
            .Select(t => t.Id)
            .Take(100) // batch limit per run
            .ToListAsync(ct);

        if (expired.Count == 0)
        {
            _logger.LogDebug("DemoCleanup: nothing expired");
            return;
        }

        _logger.LogInformation("DemoCleanup: deleting {Count} expired demo tenants", expired.Count);

        foreach (var tenantId in expired)
        {
            try
            {
                await DeleteTenantCascadeAsync(db, userManager, tenantId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete demo tenant {TenantId}", tenantId);
            }
        }
    }

    private async Task DeleteTenantCascadeAsync(
        ApplicationDbContext db, UserManager<ApplicationUser> userManager, Guid tenantId, CancellationToken ct)
    {
        // Usuń dane biznesowe per tenant. ExecuteDelete = bulk SQL DELETE, bez Track/Save.
        // Kolejność: dzieci przed rodzicami (FK).
        await db.RentalItems
            .IgnoreQueryFilters()
            .Where(i => i.Rental != null && i.Rental.TenantId == tenantId)
            .ExecuteDeleteAsync(ct);

        await db.Set<RentalReview>().IgnoreQueryFilters()
            .Where(r => r.TenantId == tenantId)
            .ExecuteDeleteAsync(ct);

        await db.Rentals.IgnoreQueryFilters()
            .Where(r => r.TenantId == tenantId).ExecuteDeleteAsync(ct);

        await db.Products.IgnoreQueryFilters()
            .Where(p => p.TenantId == tenantId).ExecuteDeleteAsync(ct);

        await db.Customers.IgnoreQueryFilters()
            .Where(c => c.TenantId == tenantId).ExecuteDeleteAsync(ct);

        await db.CompanyInfos.IgnoreQueryFilters()
            .Where(c => c.TenantId == tenantId).ExecuteDeleteAsync(ct);

        await db.Set<TenantUser>().IgnoreQueryFilters()
            .Where(tu => tu.TenantId == tenantId).ExecuteDeleteAsync(ct);

        // ApplicationUser (Identity) — usuwamy przez UserManager żeby cascade'ować role/claims/tokens.
        var users = await db.Users
            .Where(u => u.TenantId == tenantId && u.IsDemoUser)
            .ToListAsync(ct);
        foreach (var u in users)
        {
            await userManager.DeleteAsync(u);
        }

        // Tenant sam na końcu
        await db.Tenants.IgnoreQueryFilters()
            .Where(t => t.Id == tenantId).ExecuteDeleteAsync(ct);

        _logger.LogInformation("Deleted demo tenant {TenantId} + {Users} users", tenantId, users.Count);
    }
}
