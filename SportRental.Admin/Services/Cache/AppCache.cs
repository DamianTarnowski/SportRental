using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;

namespace SportRental.Admin.Services.Cache;

/// In-memory cache dla read-mostly danych per-tenant. Każdy wpis ma absolute expiration
/// (60-900s w zależności od zmienności), plus jawny `InvalidateTenant` po zapisach.
/// Cel: zredukować powtarzalne queries (Tenant.Name na każdym renderze, CompanyInfo)
/// i odciążyć vCPU + bazę.
public interface IAppCache
{
    Task<string?> GetTenantNameAsync(Guid tenantId, CancellationToken ct = default);
    Task<CompanyInfo?> GetCompanyInfoAsync(Guid tenantId, CancellationToken ct = default);
    void InvalidateTenant(Guid tenantId);
}

public class AppCache : IAppCache
{
    private readonly IMemoryCache _cache;
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;

    private static readonly TimeSpan TenantNameTtl = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan CompanyInfoTtl = TimeSpan.FromMinutes(5);

    public AppCache(IMemoryCache cache, IDbContextFactory<ApplicationDbContext> dbFactory)
    {
        _cache = cache;
        _dbFactory = dbFactory;
    }

    public async Task<string?> GetTenantNameAsync(Guid tenantId, CancellationToken ct = default)
    {
        var key = $"tenant:name:{tenantId}";
        if (_cache.TryGetValue<string?>(key, out var cached)) return cached;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var name = await db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.Name)
            .FirstOrDefaultAsync(ct);

        _cache.Set(key, name, TenantNameTtl);
        return name;
    }

    public async Task<CompanyInfo?> GetCompanyInfoAsync(Guid tenantId, CancellationToken ct = default)
    {
        var key = $"tenant:company:{tenantId}";
        if (_cache.TryGetValue<CompanyInfo?>(key, out var cached)) return cached;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.SetTenant(tenantId);
        var company = await db.CompanyInfos.AsNoTracking()
            .FirstOrDefaultAsync(ct);

        _cache.Set(key, company, CompanyInfoTtl);
        return company;
    }

    public void InvalidateTenant(Guid tenantId)
    {
        _cache.Remove($"tenant:name:{tenantId}");
        _cache.Remove($"tenant:company:{tenantId}");
    }
}
