using Microsoft.AspNetCore.SignalR;

namespace SportRental.Admin.Hubs
{
    /// <summary>
    /// SignalR hub for real-time rental status notifications.
    /// Clients join a group based on their tenant ID to receive tenant-specific updates.
    /// </summary>
    public class RentalNotificationHub : Hub
    {
        private readonly ILogger<RentalNotificationHub> _logger;

        public RentalNotificationHub(ILogger<RentalNotificationHub> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Client joins a tenant-specific group to receive notifications for that tenant only.
        /// </summary>
        public async Task JoinTenantGroup(string tenantId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"tenant_{tenantId}");
            _logger.LogDebug("Client {ConnectionId} joined tenant group {TenantId}", Context.ConnectionId, tenantId);
        }

        /// <summary>
        /// Client leaves a tenant-specific group.
        /// </summary>
        public async Task LeaveTenantGroup(string tenantId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"tenant_{tenantId}");
            _logger.LogDebug("Client {ConnectionId} left tenant group {TenantId}", Context.ConnectionId, tenantId);
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            _logger.LogDebug("Client {ConnectionId} disconnected", Context.ConnectionId);
            await base.OnDisconnectedAsync(exception);
        }
    }

    /// <summary>
    /// DTO for rental status change notification
    /// </summary>
    public record RentalStatusChangedEvent(
        Guid RentalId,
        string NewStatus,
        bool IsSmsConfirmed,
        bool IsSmsConfirmationSent,
        DateTime ChangedAtUtc
    );
}
