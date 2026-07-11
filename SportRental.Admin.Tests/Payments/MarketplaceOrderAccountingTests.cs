using FluentAssertions;
using SportRental.Admin.Payments;
using SportRental.Infrastructure.Domain;

namespace SportRental.Admin.Tests.Payments;

public sealed class MarketplaceOrderAccountingTests
{
    [Fact]
    public void ApplyRefund_AccumulatesPartialRefundsAndCapsAtCollectedDeposit()
    {
        var order = new MarketplaceOrder
        {
            DepositAmount = 100m,
            PaymentStatus = "DepositPaid"
        };
        var now = new DateTime(2026, 7, 10, 18, 0, 0, DateTimeKind.Utc);

        MarketplaceOrderAccounting.ApplyRefund(order, 60m, now);

        order.RefundedDepositAmount.Should().Be(60m);
        order.PaymentStatus.Should().Be("PartiallyRefunded");

        MarketplaceOrderAccounting.ApplyRefund(order, 50m, now.AddMinutes(1));

        order.RefundedDepositAmount.Should().Be(100m);
        order.PaymentStatus.Should().Be("Refunded");
        order.UpdatedAtUtc.Should().Be(now.AddMinutes(1));
    }

    [Fact]
    public void RentalTransitions_ReflectPartialAndFinalOrderState()
    {
        var order = new MarketplaceOrder { Status = "Confirmed" };
        var now = DateTime.UtcNow;

        MarketplaceOrderAccounting.MarkRentalCancelled(order, hasOtherActiveRental: true, now);
        order.Status.Should().Be("PartiallyCancelled");

        MarketplaceOrderAccounting.MarkRentalCancelled(order, hasOtherActiveRental: false, now);
        order.Status.Should().Be("Cancelled");

        MarketplaceOrderAccounting.MarkRentalCompleted(order, hasOtherOpenRental: true, now);
        order.Status.Should().Be("PartiallyCompleted");

        MarketplaceOrderAccounting.MarkRentalCompleted(order, hasOtherOpenRental: false, now);
        order.Status.Should().Be("Completed");
    }

    [Fact]
    public void FinalCompletion_MarksAnUnrefundedDepositAsRetained()
    {
        var order = new MarketplaceOrder
        {
            DepositAmount = 100m,
            PaymentStatus = "DepositPaid"
        };

        MarketplaceOrderAccounting.MarkRentalCompleted(
            order,
            hasOtherOpenRental: false,
            DateTime.UtcNow);

        order.PaymentStatus.Should().Be("DepositRetained");
    }
}
