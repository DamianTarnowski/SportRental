using FluentAssertions;
using Microsoft.Extensions.Options;
using SportRental.Api.Payments;
using SportRental.Shared.Models;
using Xunit;

namespace SportRental.Api.Tests;

public class StripePaymentGatewayTests
{
    [Fact]
    public async Task CreatePaymentIntent_WithStripeSandbox_ReturnsIntent()
    {
        var gateway = CreateGateway();
        var tenantId = Guid.NewGuid();

        var intent = await gateway.CreatePaymentIntentAsync(
            tenantId: tenantId,
            amount: 150m,
            depositAmount: 45m,
            currency: "pln");

        intent.Should().NotBeNull();
        intent.Id.Should().StartWith("pi_");
        intent.Amount.Should().Be(150m);
        intent.DepositAmount.Should().Be(45m);
    }

    [Fact]
    public async Task CapturePaymentIntent_WithConfirmedIntent_Succeeds()
    {
        var gateway = CreateGateway();
        var tenantId = Guid.NewGuid();

        var intent = await gateway.CreatePaymentIntentAsync(tenantId, 120m, 36m, "pln");
        await StripeTestHelper.ConfirmPaymentIntentAsync(intent.Id);

        var captured = await gateway.CapturePaymentAsync(tenantId, intent.Id);
        captured.Should().BeTrue();
    }

    [Fact]
    public async Task CancelPaymentIntent_BeforeConfirmation_Succeeds()
    {
        var gateway = CreateGateway();
        var tenantId = Guid.NewGuid();

        var intent = await gateway.CreatePaymentIntentAsync(tenantId, 80m, 24m, "pln");

        var cancelled = await gateway.CancelPaymentAsync(tenantId, intent.Id);
        cancelled.Should().BeTrue();
    }

    [Fact]
    public async Task GetPaymentIntent_AfterCreation_ReturnsDetails()
    {
        var gateway = CreateGateway();
        var tenantId = Guid.NewGuid();

        var created = await gateway.CreatePaymentIntentAsync(tenantId, 90m, 27m, "pln");
        var fetched = await gateway.GetPaymentIntentAsync(tenantId, created.Id);

        fetched.Should().NotBeNull();
        fetched!.Id.Should().Be(created.Id);
    }

    [Fact]
    public async Task CreatePaymentIntent_WithMixedTenants_AllowsEmptyTenant()
    {
        var gateway = CreateGateway();
        var tenantId = Guid.Empty; // multi-tenant checkout uses empty tenant on intent

        var intent = await gateway.CreatePaymentIntentAsync(tenantId, 200m, 60m, "pln");
        intent.Should().NotBeNull();
        intent.Id.Should().StartWith("pi_");

        await StripeTestHelper.ConfirmPaymentIntentAsync(intent.Id);
        var captured = await gateway.CapturePaymentAsync(tenantId, intent.Id);
        captured.Should().BeTrue();

        var fetched = await gateway.GetPaymentIntentAsync(tenantId, intent.Id);
        fetched.Should().NotBeNull();
        fetched!.Status.Should().BeOneOf(PaymentIntentStatus.Succeeded, PaymentIntentStatus.RequiresCapture, PaymentIntentStatus.Processing);
    }

    private static StripePaymentGateway CreateGateway()
    {
        var options = StripeTestHelper.GetStripeOptions();
        return new StripePaymentGateway(Options.Create(options));
    }
}
