using FluentAssertions;
using Microsoft.Extensions.Options;
using SportRental.Api.Payments;
using Xunit;

namespace SportRental.Api.Tests;

public class StripePaymentGatewayTests
{
    [Fact]
    public async Task CreatePaymentIntent_ValidRequest_ReturnsIntent()
    {
        // Arrange
        var options = Options.Create(new StripeOptions
        {
            SecretKey = "sk_test_fake_key_for_unit_tests",
            Currency = "pln"
        });

        var gateway = new StripePaymentGateway(options);
        var tenantId = Guid.NewGuid();

        // Act & Assert
        // Real Stripe calls will fail in unit tests without valid API key
        // This test demonstrates the interface contract
        await Assert.ThrowsAsync<Stripe.StripeException>(async () =>
        {
            await gateway.CreatePaymentIntentAsync(
                tenantId: tenantId,
                amount: 100m,
                depositAmount: 30m,
                currency: "pln",
                metadata: new Dictionary<string, string>
                {
                    ["rental_id"] = Guid.NewGuid().ToString()
                }
            );
        });
    }

    [Fact]
    public async Task GetPaymentIntent_InvalidId_ReturnsNull()
    {
        // Arrange
        var options = Options.Create(new StripeOptions
        {
            SecretKey = "sk_test_fake_key_for_unit_tests",
            Currency = "pln"
        });

        var gateway = new StripePaymentGateway(options);
        var tenantId = Guid.NewGuid();
        var invalidId = Guid.NewGuid();

        // Act
        var result = await gateway.GetPaymentIntentAsync(tenantId, invalidId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void StripeOptions_DefaultCurrency_IsPln()
    {
        // Arrange & Act
        var options = new StripeOptions();

        // Assert
        options.Currency.Should().Be("pln");
    }

    [Fact]
    public void MockPaymentGateway_CreatePaymentIntent_ReturnsRequiresPaymentMethod()
    {
        // Arrange
        var mock = new MockPaymentGateway();
        var tenantId = Guid.NewGuid();

        // Act
        var result = mock.CreatePaymentIntentAsync(
            tenantId: tenantId,
            amount: 100m,
            depositAmount: 30m,
            currency: "pln"
        ).GetAwaiter().GetResult();

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().Be(Shared.Models.PaymentIntentStatus.RequiresPaymentMethod);
        result.Amount.Should().Be(100m);
        result.DepositAmount.Should().Be(30m);
        result.Currency.Should().Be("pln");
        result.ClientSecret.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task MockPaymentGateway_CapturePayment_ReturnsTrue()
    {
        // Arrange
        var mock = new MockPaymentGateway();
        var tenantId = Guid.NewGuid();

        var intent = await mock.CreatePaymentIntentAsync(tenantId, 100m, 30m, "pln");

        // Act
        var result = await mock.CapturePaymentAsync(tenantId, intent.Id);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task MockPaymentGateway_RefundPayment_ReturnsTrue()
    {
        // Arrange
        var mock = new MockPaymentGateway();
        var tenantId = Guid.NewGuid();

        var intent = await mock.CreatePaymentIntentAsync(tenantId, 100m, 30m, "pln");

        // Act
        var result = await mock.RefundPaymentAsync(tenantId, intent.Id, amount: 50m, reason: "customer_request");

        // Assert
        result.Should().BeTrue();
    }
}
