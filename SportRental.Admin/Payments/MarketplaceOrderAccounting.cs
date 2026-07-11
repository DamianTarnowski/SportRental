using SportRental.Infrastructure.Domain;

namespace SportRental.Admin.Payments;

/// <summary>
/// Centralizes state transitions of the cross-tenant order. Callers must hold
/// the MarketplaceOrders row lock while applying a transition.
/// </summary>
internal static class MarketplaceOrderAccounting
{
    public static void ApplyRefund(MarketplaceOrder order, decimal amount, DateTime nowUtc)
    {
        if (amount < 0m)
            throw new ArgumentOutOfRangeException(nameof(amount));

        if (amount > 0m)
        {
            order.RefundedDepositAmount = Math.Min(
                order.DepositAmount,
                order.RefundedDepositAmount + amount);
            order.PaymentStatus = order.RefundedDepositAmount >= order.DepositAmount
                ? "Refunded"
                : "PartiallyRefunded";
        }

        order.UpdatedAtUtc = nowUtc;
    }

    public static void MarkRentalCancelled(
        MarketplaceOrder order,
        bool hasOtherActiveRental,
        DateTime nowUtc)
    {
        order.Status = hasOtherActiveRental ? "PartiallyCancelled" : "Cancelled";
        order.UpdatedAtUtc = nowUtc;
    }

    public static void MarkRentalCompleted(
        MarketplaceOrder order,
        bool hasOtherOpenRental,
        DateTime nowUtc)
    {
        order.Status = hasOtherOpenRental ? "PartiallyCompleted" : "Completed";
        if (!hasOtherOpenRental &&
            order.DepositAmount > 0m &&
            order.RefundedDepositAmount == 0m &&
            string.Equals(order.PaymentStatus, "DepositPaid", StringComparison.Ordinal))
        {
            order.PaymentStatus = "DepositRetained";
        }
        order.UpdatedAtUtc = nowUtc;
    }
}
