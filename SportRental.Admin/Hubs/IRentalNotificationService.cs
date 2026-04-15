namespace SportRental.Admin.Hubs
{
    /// <summary>
    /// Service for sending rental status change notifications via SignalR.
    /// </summary>
    public interface IRentalNotificationService
    {
        /// <summary>
        /// Notifies all clients in a tenant group about a rental status change.
        /// </summary>
        Task NotifyRentalStatusChangedAsync(Guid tenantId, RentalStatusChangedEvent evt, CancellationToken ct = default);
    }
}
