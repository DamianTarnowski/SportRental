using Microsoft.EntityFrameworkCore;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Tenancy;

namespace SportRental.Admin.Services.Demo;

/// Sprawdza czy aktualnie zalogowany user/tenant to demo.
/// Używane w SMS/Email senderach: w demo nie wysyłamy realnych wiadomości,
/// tylko logujemy "co by poszło".
public interface IDemoGuard
{
    Task<bool> IsCurrentTenantDemoAsync(CancellationToken ct = default);
}

public class DemoGuard : IDemoGuard
{
    private readonly ITenantProvider _tenantProvider;
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
    private readonly ILogger<DemoGuard> _logger;

    // Per-circuit cache: tenant ID → IsDemo. Demo flag nie zmienia się w czasie życia tenanta.
    private static readonly Dictionary<Guid, bool> _cache = new();
    private static readonly object _lock = new();

    public DemoGuard(
        ITenantProvider tenantProvider,
        IDbContextFactory<ApplicationDbContext> dbFactory,
        ILogger<DemoGuard> logger)
    {
        _tenantProvider = tenantProvider;
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task<bool> IsCurrentTenantDemoAsync(CancellationToken ct = default)
    {
        var tenantId = _tenantProvider.GetCurrentTenantId();
        if (tenantId is null) return false;

        lock (_lock)
        {
            if (_cache.TryGetValue(tenantId.Value, out var cached)) return cached;
        }

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var isDemo = await db.Tenants
                .IgnoreQueryFilters()
                .Where(t => t.Id == tenantId.Value)
                .Select(t => t.IsDemo)
                .FirstOrDefaultAsync(ct);

            lock (_lock) { _cache[tenantId.Value] = isDemo; }
            return isDemo;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DemoGuard lookup failed for tenant {TenantId}", tenantId);
            return false; // fail safe — w razie wątpliwości NIE blokuj normalnych wysyłek
        }
    }
}
