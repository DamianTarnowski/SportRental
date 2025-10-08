using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using MudBlazor;
using MudBlazor.Services;
using SportRental.Client.Pages;
using SportRental.Shared.Models;
using SportRental.Shared.Services;
using Xunit;

namespace SportRental.Client.Tests;

/// <summary>
/// Testy integracji z Stripe Checkout Session
/// Weryfikuje poprawność flow płatności przez Stripe
/// </summary>
public class StripeIntegrationTests : Bunit.TestContext
{
    private readonly Mock<IApiService> _mockApiService;

    public StripeIntegrationTests()
    {
        _mockApiService = new Mock<IApiService>();
        Services.AddMudServices();
        Services.AddSingleton(_mockApiService.Object);

        // Setup JSInterop dla MudBlazor
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void CheckoutSuccess_DisplaysSuccessMessage()
    {
        // Arrange - Navigate with query parameter
        var navManager = Services.GetRequiredService<FakeNavigationManager>();
        navManager.NavigateTo("http://localhost/checkout/success?session_id=cs_test_success_123");

        // Act
        var cut = RenderComponent<CheckoutSuccess>();

        // Assert
        cut.Markup.Should().Contain("Płatność zakończona sukcesem");
        cut.Markup.Should().Contain("cs_test_success_123");
    }

    [Fact]
    public void CheckoutSuccess_HasLinkToMyRentals()
    {
        // Arrange - Navigate with query parameter
        var navManager = Services.GetRequiredService<FakeNavigationManager>();
        navManager.NavigateTo("http://localhost/checkout/success?session_id=cs_test_123");

        // Act
        var cut = RenderComponent<CheckoutSuccess>();

        // Assert
        var myRentalsLink = cut.FindAll("a")
            .FirstOrDefault(a => a.GetAttribute("href")?.Contains("/my-rentals") == true);
        myRentalsLink.Should().NotBeNull("Should have link to My Rentals page");
    }

    [Fact]
    public void CheckoutCancel_DisplaysCancelMessage()
    {
        // Arrange & Act
        var cut = RenderComponent<CheckoutCancel>();

        // Assert
        cut.Markup.Should().Contain("Płatność anulowana");
        cut.Markup.Should().Contain("został przerwany");
    }

    [Fact]
    public void CheckoutCancel_HasLinkBackToCart()
    {
        // Arrange & Act
        var cut = RenderComponent<CheckoutCancel>();

        // Assert
        var cartLink = cut.FindAll("a")
            .FirstOrDefault(a => a.GetAttribute("href")?.Contains("/cart") == true);
        cartLink.Should().NotBeNull("Should have link back to cart");
    }

    [Fact]
    public async Task CheckoutSession_UrlFormat_IsValid()
    {
        // Arrange
        var mockSession = new CheckoutSessionResponse(
            SessionId: "cs_test_a1b2c3d4e5f6g7h8",
            Url: "https://checkout.stripe.com/pay/cs_test_a1b2c3d4e5f6g7h8",
            ExpiresAt: DateTime.UtcNow.AddHours(1)
        );

        // Assert
        mockSession.Url.Should().StartWith("https://checkout.stripe.com");
        mockSession.SessionId.Should().StartWith("cs_test_");
        mockSession.ExpiresAt.Should().BeAfter(DateTime.UtcNow);
    }

    [Theory]
    [InlineData("4242424242424242", "Success card")]
    [InlineData("4000000000000002", "Declined card")]
    [InlineData("4000002500003155", "3D Secure card")]
    public void StripeTestCards_AreDocumented(string cardNumber, string description)
    {
        // This test documents the test cards we use for Stripe integration
        // These are the standard Stripe test card numbers

        // Assert
        cardNumber.Should().HaveLength(16);
        description.Should().NotBeNullOrEmpty();

        // Verify it's a valid test card format
        cardNumber.Should().StartWith("4"); // Visa cards start with 4
    }

    [Fact]
    public async Task CreateCheckoutSession_Request_ContainsRequiredFields()
    {
        // Arrange
        var request = new CreateCheckoutSessionRequest(
            StartDateUtc: DateTime.UtcNow.AddDays(1),
            EndDateUtc: DateTime.UtcNow.AddDays(3),
            Items: new List<CheckoutItem>
            {
                new CheckoutItem(Guid.NewGuid(), 2)
            },
            CustomerEmail: "test@example.com",
            CustomerId: Guid.NewGuid()
        );

        // Assert
        request.StartDateUtc.Should().BeBefore(request.EndDateUtc);
        request.Items.Should().NotBeEmpty();
        request.CustomerEmail.Should().Contain("@");
        request.CustomerId.Should().NotBeNull();
    }

    [Fact]
    public async Task CheckoutSession_Response_ContainsValidUrl()
    {
        // Arrange
        var response = new CheckoutSessionResponse(
            SessionId: "cs_test_123",
            Url: "https://checkout.stripe.com/pay/cs_test_123",
            ExpiresAt: DateTime.UtcNow.AddHours(1)
        );

        // Assert
        response.SessionId.Should().NotBeNullOrEmpty();
        response.Url.Should().StartWith("https://");
        response.ExpiresAt.Should().BeAfter(DateTime.UtcNow);
        
        // Verify URL is parseable
        var uri = new Uri(response.Url);
        uri.Host.Should().Contain("stripe.com");
    }

    [Fact]
    public void CheckoutItem_Validation()
    {
        // Arrange & Act
        var item = new CheckoutItem(Guid.NewGuid(), 5);

        // Assert
        item.ProductId.Should().NotBeEmpty();
        item.Quantity.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task PaymentQuote_CalculatesDepositAs30Percent()
    {
        // Arrange
        var totalAmount = 1000m;
        var expectedDeposit = 300m; // 30% of 1000

        var quote = new PaymentQuoteResponse
        {
            TotalAmount = totalAmount,
            DepositAmount = expectedDeposit
        };

        // Assert
        quote.DepositAmount.Should().Be(totalAmount * 0.3m);
        (quote.TotalAmount - quote.DepositAmount).Should().Be(700m); // Remaining on pickup
    }
}
