using SportRental.Shared.Models;

namespace SportRental.Admin.Payments;

/// <summary>
/// Payment gateway interface for rental deposits and final payments
/// </summary>
public interface IPaymentGateway
{
    /// <summary>
    /// Creates a payment intent for rental deposit
    /// </summary>
    Task<PaymentIntentDto> CreatePaymentIntentAsync(Guid tenantId, decimal amount, decimal depositAmount, string currency, Dictionary<string, string>? metadata = null);
    
    /// <summary>
    /// Retrieves an existing payment intent
    /// </summary>
    Task<PaymentIntentDto?> GetPaymentIntentAsync(Guid tenantId, string id);
    
    /// <summary>
    /// Confirms/captures a payment intent
    /// </summary>
    Task<bool> CapturePaymentAsync(Guid tenantId, string id);
    
    /// <summary>
    /// Cancels a payment intent
    /// </summary>
    Task<bool> CancelPaymentAsync(Guid tenantId, string id);
    
    /// <summary>
    /// Creates a refund for a captured payment
    /// </summary>
    Task<bool> RefundPaymentAsync(Guid tenantId, string id, decimal? amount = null, string? reason = null);
}
