using SportRental.Admin.Services.Guards;
using SportRental.Infrastructure.Domain;

namespace SportRental.Admin.Tests.Services.Guards;

public class RentalGuardsPaymentTests
{
    [Fact]
    public void DepositPaid_IsPartialAndBlocksIssueUntilBalanceIsPaid()
    {
        var rental = CreateRental(total: 500m, deposit: 150m, paid: 0m, status: "DepositPaid");

        Assert.False(RentalGuards.IsRentalPaid(rental));
        Assert.False(RentalGuards.HasAnyPayment(rental));
        Assert.True(RentalGuards.IsDepositCollected(rental));
        Assert.Equal(500m, RentalGuards.GetOutstandingAmount(rental));
        Assert.Contains("Brak płatności", RentalGuards.GetIssueBlockReason(rental));
    }

    [Fact]
    public void PaidAmountEqualToTotal_IsFullyPaid()
    {
        var rental = CreateRental(total: 500m, deposit: 150m, paid: 500m, status: "Paid");

        Assert.True(RentalGuards.IsRentalPaid(rental));
        Assert.Equal(0m, RentalGuards.GetOutstandingAmount(rental));
        Assert.Null(RentalGuards.GetIssueBlockReason(rental));
    }

    [Fact]
    public void RetainedDeposit_CoversDamageBeforeCreatingOutstandingBalance()
    {
        var rental = CreateRental(total: 500m, deposit: 300m, paid: 500m, status: "DepositPaid");
        rental.DepositAmount = 300m;
        rental.DepositPaidAtUtc = DateTime.UtcNow.AddDays(-1);
        rental.ReturnDepositRefund = 200m;
        rental.DamageCharge = 150m;

        Assert.Equal(50m, RentalGuards.GetOutstandingAmount(rental));
    }

    [Theory]
    [InlineData("DepositRefunded")]
    [InlineData("Refunded")]
    public void RefundedStatuses_HaveNoCurrentPayment(string status)
    {
        var rental = CreateRental(total: 500m, deposit: 150m, paid: 150m, status: status);

        Assert.Equal(0m, RentalGuards.GetPaidAmount(rental));
        Assert.False(RentalGuards.HasAnyPayment(rental));
    }

    [Fact]
    public void LegacyDepositPaid_UsesDepositWhenPaidAmountWasNotStored()
    {
        var rental = CreateRental(total: 500m, deposit: 150m, paid: 0m, status: "DepositPaid");

        Assert.Equal(0m, RentalGuards.GetPaidAmount(rental));
        Assert.True(RentalGuards.IsDepositCollected(rental));
        Assert.False(RentalGuards.IsRentalPaid(rental));
    }

    private static Rental CreateRental(decimal total, decimal deposit, decimal paid, string status) => new()
    {
        TotalAmount = total,
        DepositAmount = deposit,
        PaidAmount = paid,
        PaymentStatus = status,
        Status = RentalStatus.Confirmed,
        ContractUrl = "contract.pdf"
    };
}
