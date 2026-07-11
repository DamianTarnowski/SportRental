using SportRental.Admin.Payments;

namespace SportRental.Admin.Tests.Payments;

public class CheckoutIdempotencyKeyTests
{
    [Fact]
    public void Create_IsStableRegardlessOfHoldOrder()
    {
        var customerId = Guid.NewGuid();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        var a = CheckoutIdempotencyKey.Create(customerId, [first, second]);
        var b = CheckoutIdempotencyKey.Create(customerId, [second, first]);

        Assert.Equal(a, b);
        Assert.StartsWith("checkout:", a);
        Assert.Equal(73, a.Length);
    }

    [Fact]
    public void Create_ChangesForDifferentCustomerOrHolds()
    {
        var customerId = Guid.NewGuid();
        var holdId = Guid.NewGuid();
        var baseline = CheckoutIdempotencyKey.Create(customerId, [holdId]);

        Assert.NotEqual(baseline, CheckoutIdempotencyKey.Create(Guid.NewGuid(), [holdId]));
        Assert.NotEqual(baseline, CheckoutIdempotencyKey.Create(customerId, [Guid.NewGuid()]));
    }
}
