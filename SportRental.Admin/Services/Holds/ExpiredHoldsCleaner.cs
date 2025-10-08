using SportRental.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SportRental.Admin.Services.Holds
{
    public class ExpiredHoldsCleaner : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ExpiredHoldsCleaner> _logger;

        public ExpiredHoldsCleaner(IServiceScopeFactory scopeFactory, ILogger<ExpiredHoldsCleaner> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
                    await using var db = await dbFactory.CreateDbContextAsync(stoppingToken);
                    var now = DateTime.UtcNow;
                    var expired = await db.ReservationHolds
                        .Where(h => h.ExpiresAtUtc <= now)
                        .ToListAsync(stoppingToken);
                    if (expired.Count > 0)
                    {
                        db.ReservationHolds.RemoveRange(expired);
                        await db.SaveChangesAsync(stoppingToken);
                        _logger.LogInformation("ExpiredHoldsCleaner: removed {Count} holds", expired.Count);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "ExpiredHoldsCleaner error");
                }

                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }
}
