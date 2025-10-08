using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;
using SportRental.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace SportRental.Admin.Services.Logging
{
    public class DatabaseAuditLogger : IAuditLogger
    {
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
        private readonly ITenantProvider _tenantProvider;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<DatabaseAuditLogger> _logger;

        public DatabaseAuditLogger(
            IDbContextFactory<ApplicationDbContext> contextFactory,
            ITenantProvider tenantProvider,
            IHttpContextAccessor httpContextAccessor,
            UserManager<ApplicationUser> userManager,
            ILogger<DatabaseAuditLogger> logger)
        {
            _contextFactory = contextFactory;
            _tenantProvider = tenantProvider;
            _httpContextAccessor = httpContextAccessor;
            _userManager = userManager;
            _logger = logger;
        }

        public async Task LogAsync(string message, string? action = null, string? entityType = null, Guid? entityId = null, string level = "Info", CancellationToken cancellationToken = default)
        {
            try
            {
                var tenantId = _tenantProvider.GetCurrentTenantId();
                if (tenantId == null) return;

                var userId = GetCurrentUserId();
                var userGuid = await GetCurrentUserGuidAsync();

                using var context = _contextFactory.CreateDbContext();
                context.SetTenant(tenantId);

                var auditLog = new AuditLog
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId.Value,
                    Message = message,
                    Action = action,
                    EntityType = entityType,
                    EntityId = entityId,
                    Level = level,
                    UserId = userId,
                    UserGuid = userGuid,
                    Date = DateTime.UtcNow
                };

                context.AuditLogs.Add(auditLog);
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write audit log: {Message}", message);
            }
        }

        public async Task LogErrorAsync(string message, Exception? exception = null, string? source = null, string severity = "Error", CancellationToken cancellationToken = default)
        {
            try
            {
                var tenantId = _tenantProvider.GetCurrentTenantId();
                if (tenantId == null) return;

                var userId = GetCurrentUserId();
                var userGuid = await GetCurrentUserGuidAsync();

                using var context = _contextFactory.CreateDbContext();
                context.SetTenant(tenantId);

                var errorLog = new ErrorLog
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId.Value,
                    Message = message,
                    StackTrace = exception?.StackTrace,
                    Source = source ?? exception?.Source,
                    Severity = severity,
                    UserId = userId,
                    UserGuid = userGuid,
                    Date = DateTime.UtcNow
                };

                context.ErrorLogs.Add(errorLog);
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write error log: {Message}", message);
            }
        }

        public async Task LogUserActionAsync(string action, string entityType, Guid entityId, string message, CancellationToken cancellationToken = default)
        {
            await LogAsync(message, action, entityType, entityId, "Info", cancellationToken);
        }

        private string? GetCurrentUserId()
        {
            return _httpContextAccessor.HttpContext?.User?.Identity?.Name;
        }

        private async Task<Guid?> GetCurrentUserGuidAsync()
        {
            var principal = _httpContextAccessor.HttpContext?.User;
            if (principal == null)
            {
                return null;
            }

            var identity = principal.Identity;
            if (identity == null)
            {
                return null;
            }

            if (identity.IsAuthenticated != true && string.IsNullOrWhiteSpace(identity.Name))
            {
                return null;
            }

            try
            {
                var user = await _userManager.GetUserAsync(principal);
                return user?.Id;
            }
            catch
            {
                return null;
            }
        }
    }
}



