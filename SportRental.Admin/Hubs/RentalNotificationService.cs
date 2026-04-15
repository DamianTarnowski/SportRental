using Microsoft.AspNetCore.SignalR;

namespace SportRental.Admin.Hubs
{
    /// <summary>
    /// Implementation of IRentalNotificationService that sends notifications via SignalR hub.
    /// </summary>
    public class RentalNotificationService : IRentalNotificationService
    {
        private readonly IHubContext<RentalNotificationHub> _hubContext;
        private readonly ILogger<RentalNotificationService> _logger;

        public RentalNotificationService(
            IHubContext<RentalNotificationHub> hubContext,
            ILogger<RentalNotificationService> logger)
        {
            _hubContext = hubContext;
            _logger = logger;
        }

        public async Task NotifyRentalStatusChangedAsync(Guid tenantId, RentalStatusChangedEvent evt, CancellationToken ct = default)
        {
            var groupName = $"tenant_{tenantId}";
            
            try
            {
                await _hubContext.Clients.Group(groupName)
                    .SendAsync("RentalStatusChanged", evt, ct);
                
                _logger.LogDebug(
                    "Sent RentalStatusChanged notification for rental {RentalId} to group {Group}",
                    evt.RentalId, groupName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, 
                    "Failed to send RentalStatusChanged notification for rental {RentalId}", 
                    evt.RentalId);
            }
        }
    }
}
