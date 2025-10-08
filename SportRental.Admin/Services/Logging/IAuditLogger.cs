using SportRental.Infrastructure.Domain;

namespace SportRental.Admin.Services.Logging
{
    public interface IAuditLogger
    {
        Task LogAsync(string message, string? action = null, string? entityType = null, Guid? entityId = null, string level = "Info", CancellationToken cancellationToken = default);
        Task LogErrorAsync(string message, Exception? exception = null, string? source = null, string severity = "Error", CancellationToken cancellationToken = default);
        Task LogUserActionAsync(string action, string entityType, Guid entityId, string message, CancellationToken cancellationToken = default);
    }
}