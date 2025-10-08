using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SportRental.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace SportRental.Admin.Health
{
    public sealed class DbHealthCheck : IHealthCheck
    {
        private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
        public DbHealthCheck(IDbContextFactory<ApplicationDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
                // Prosty lekki check: czy możemy wykonać zapytanie
                var canConnect = await db.Database.CanConnectAsync(cancellationToken);
                return canConnect
                    ? HealthCheckResult.Healthy("DB reachable")
                    : HealthCheckResult.Unhealthy("DB unreachable");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("DB check failed", ex);
            }
        }
    }
}
