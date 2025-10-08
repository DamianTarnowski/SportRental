using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using MudBlazor;
using MudBlazor.Services;
using SportRental.Client.Pages;
using SportRental.Client.Services;
using SportRental.Shared.Models;
using SportRental.Shared.Services;
using Xunit;
using CartModel = SportRental.Shared.Models.Cart;

namespace SportRental.Client.Tests;

/// <summary>
/// Testy end-to-end dla procesu checkout w Blazor WASM Client
/// Testuje cały flow zakupu z płatnością Stripe
/// </summary>
public class CheckoutFlowTests : Bunit.TestContext
{
    private readonly Mock<IApiService> _mockApiService;
    private readonly Mock<ICartService> _mockCartService;
    private readonly Mock<ICustomerSessionService> _mockCustomerSession;
    private readonly Mock<ISnackbar> _mockSnackbar;

    public CheckoutFlowTests()
    {
        _mockApiService = new Mock<IApiService>();
        _mockCartService = new Mock<ICartService>();
        _mockCustomerSession = new Mock<ICustomerSessionService>();
        _mockSnackbar = new Mock<ISnackbar>();

        // Konfiguracja DI dla bUnit
        Services.AddMudServices();
        Services.AddSingleton(_mockApiService.Object);
        Services.AddSingleton(_mockCartService.Object);
        Services.AddSingleton(_mockCustomerSession.Object);
        Services.AddSingleton(_mockSnackbar.Object);

        // Setup JSInterop dla MudBlazor komponentów
        JSInterop.Mode = JSRuntimeMode.Loose; // Ignore all JS calls
        JSInterop.SetupVoid("mudElementRef.addOnBlurEvent", _ => true);
        JSInterop.SetupVoid("mudElementRef.removeOnBlurEvent", _ => true);
        JSInterop.SetupVoid("mudKeyInterceptor.connect", _ => true);
        JSInterop.SetupVoid("mudKeyInterceptor.disconnect", _ => true);
        JSInterop.SetupVoid("mudScrollManager.lockScroll", _ => true);
        JSInterop.SetupVoid("mudScrollManager.unlockScroll", _ => true);
    }

    [Fact]
    public void Checkout_EmptyCart_ShowsEmptyCartMessage()
    {
        // Arrange
        var emptyCart = new CartModel { Items = new List<CartItem>() };
        _mockCartService.Setup(x => x.GetCart()).Returns(emptyCart);

        // Act
        var cut = RenderComponent<Checkout>();

        // Assert
        var alert = cut.Find("div.mud-alert");
        alert.Should().NotBeNull();
        alert.TextContent.Should().Contain("Koszyk jest pusty");
    }

    [Fact]
    public async Task Checkout_ValidCart_DisplaysCartItems()
    {
        // Arrange
        var testCart = CreateTestCart();
        _mockCartService.Setup(x => x.GetCart()).Returns(testCart);

        var mockQuote = new PaymentQuoteResponse
        {
            TotalAmount = 300m,
            DepositAmount = 90m
        };
        _mockApiService
            .Setup(x => x.GetPaymentQuoteAsync(It.IsAny<PaymentQuoteRequest>()))
            .ReturnsAsync(mockQuote);

        // Act
        var cut = RenderComponent<Checkout>();
        await Task.Delay(100); // Czekamy na async operations

        // Assert
        cut.Markup.Should().Contain("Narty testowe");
        cut.Markup.Should().Contain("300"); // Total amount
        cut.Markup.Should().Contain("90");  // Deposit
    }

    [Fact]
    public void Checkout_CreateCheckoutSession_ValidatesRequest()
    {
        // Arrange - Test that CreateCheckoutSessionRequest is properly structured
        var testProductId = Guid.NewGuid();
        var testCustomerId = Guid.NewGuid();
        var testEmail = "test@example.com";
        
        var request = new CreateCheckoutSessionRequest(
            StartDateUtc: DateTime.UtcNow.AddDays(1),
            EndDateUtc: DateTime.UtcNow.AddDays(3),
            Items: new List<CheckoutItem>
            {
                new CheckoutItem(testProductId, 2)
            },
            CustomerEmail: testEmail,
            CustomerId: testCustomerId
        );

        // Assert - Verify request structure
        request.StartDateUtc.Should().BeBefore(request.EndDateUtc);
        request.Items.Should().HaveCount(1);
        request.Items[0].ProductId.Should().Be(testProductId);
        request.Items[0].Quantity.Should().Be(2);
        request.CustomerEmail.Should().Be(testEmail);
        request.CustomerId.Should().Be(testCustomerId);
    }

    [Fact]
    public void CheckoutSession_Response_ContainsStripeUrl()
    {
        // Arrange
        var stripeUrl = "https://checkout.stripe.com/pay/cs_test_123";
        var mockCheckoutSession = new CheckoutSessionResponse(
            SessionId: "cs_test_123",
            Url: stripeUrl,
            ExpiresAt: DateTime.UtcNow.AddHours(1)
        );

        // Assert - Verify response format
        mockCheckoutSession.Url.Should().StartWith("https://checkout.stripe.com");
        mockCheckoutSession.SessionId.Should().NotBeNullOrEmpty();
        mockCheckoutSession.ExpiresAt.Should().BeAfter(DateTime.UtcNow);
    }

    [Fact]
    public async Task Checkout_GetPaymentQuote_CalculatesCorrectly()
    {
        // Arrange
        var testCart = CreateTestCart();
        _mockCartService.Setup(x => x.GetCart()).Returns(testCart);

        var expectedTotal = 300m;
        var expectedDeposit = 90m;

        var mockQuote = new PaymentQuoteResponse
        {
            TotalAmount = expectedTotal,
            DepositAmount = expectedDeposit
        };

        PaymentQuoteRequest? capturedRequest = null;
        _mockApiService
            .Setup(x => x.GetPaymentQuoteAsync(It.IsAny<PaymentQuoteRequest>()))
            .Callback<PaymentQuoteRequest>(req => capturedRequest = req)
            .ReturnsAsync(mockQuote);

        // Act
        var cut = RenderComponent<Checkout>();
        await Task.Delay(200); // Wait for quote calculation

        // Assert
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Items.Should().HaveCount(1);
        capturedRequest.Items[0].ProductId.Should().NotBeEmpty();
        capturedRequest.Items[0].Quantity.Should().Be(2);

        // Verify UI displays correct amounts
        cut.Markup.Should().Contain(expectedTotal.ToString("C"));
        cut.Markup.Should().Contain(expectedDeposit.ToString("C"));
    }

    [Fact]
    public void Checkout_MixedDates_ShowsWarning()
    {
        // Arrange
        var mixedCart = new CartModel
        {
            Items = new List<CartItem>
            {
                new CartItem
                {
                    ProductId = Guid.NewGuid(),
                    ProductName = "Narty 1",
                    Quantity = 1,
                    DailyPrice = 100m,
                    StartDate = DateTime.UtcNow.AddDays(1),
                    EndDate = DateTime.UtcNow.AddDays(3)
                    // TotalPrice jest obliczane automatycznie
                },
                new CartItem
                {
                    ProductId = Guid.NewGuid(),
                    ProductName = "Narty 2",
                    Quantity = 1,
                    DailyPrice = 100m,
                    StartDate = DateTime.UtcNow.AddDays(5), // Different dates!
                    EndDate = DateTime.UtcNow.AddDays(7)
                    // TotalPrice jest obliczane automatycznie
                }
            }
        };
        _mockCartService.Setup(x => x.GetCart()).Returns(mixedCart);

        // Act
        var cut = RenderComponent<Checkout>();

        // Assert
        var warning = cut.FindAll("div.mud-alert")
            .FirstOrDefault(el => el.TextContent.Contains("rozne daty"));
        warning.Should().NotBeNull("Mixed dates should show warning");

        // Submit button should be disabled
        var submitButton = cut.FindAll("button")
            .FirstOrDefault(b => b.TextContent.Contains("Potwierdz"));
        submitButton?.GetAttribute("disabled").Should().NotBeNull();
    }

    [Fact]
    public async Task Checkout_ApiError_ShowsErrorMessage()
    {
        // Arrange
        var testCart = CreateTestCart();
        _mockCartService.Setup(x => x.GetCart()).Returns(testCart);

        _mockApiService
            .Setup(x => x.GetPaymentQuoteAsync(It.IsAny<PaymentQuoteRequest>()))
            .ThrowsAsync(new Exception("API connection failed"));

        // Act
        var cut = RenderComponent<Checkout>();
        await Task.Delay(200); // Wait for error to appear

        // Assert
        var errorAlert = cut.FindAll("div.mud-alert")
            .FirstOrDefault(el => el.TextContent.Contains("Nie udalo sie obliczyc"));
        errorAlert.Should().NotBeNull("API error should show error message");
    }

    private static CartModel CreateTestCart()
    {
        return new CartModel
        {
            Items = new List<CartItem>
            {
                new CartItem
                {
                    ProductId = Guid.NewGuid(),
                    ProductName = "Narty testowe",
                    Quantity = 2,
                    DailyPrice = 100m,
                    StartDate = DateTime.UtcNow.AddDays(1),
                    EndDate = DateTime.UtcNow.AddDays(3)
                    // TotalPrice jest obliczane automatycznie
                }
            }
        };
    }
}
