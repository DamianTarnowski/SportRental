using System.Globalization;
using System.Security.Claims;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using MudBlazor;
using MudBlazor.Services;
using SportRental.Client.Pages;
using SportRental.Client.Services;
using SportRental.Shared.Models;
using SportRental.Shared.Services;
using SportRental.Shared.Time;
using CartModel = SportRental.Shared.Models.Cart;

namespace SportRental.Client.Tests.Pages;

public sealed class CheckoutPresentationTests : TestContext
{
    private readonly Mock<IApiService> _api = new();
    private readonly Mock<ICartService> _cart = new();
    private readonly Mock<ICustomerSessionService> _customerSession = new();

    public CheckoutPresentationTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton(_api.Object);
        Services.AddSingleton(_cart.Object);
        Services.AddSingleton(_customerSession.Object);
        Services.AddSingleton<AuthenticationStateProvider>(new AnonymousAuthenticationStateProvider());
        _cart
            .Setup(service => service.RefreshHoldsIfNeededAsync(It.IsAny<TimeSpan?>()))
            .Returns(Task.CompletedTask);
    }

    [Fact]
    public void Checkout_UsesQuotedItemSubtotalAndExplicitPolishCurrencyFormatting()
    {
        var productId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var cart = new CartModel
        {
            Items =
            [
                new CartItem
                {
                    ProductId = productId,
                    ProductName = "Narty sezonowe",
                    TenantId = tenantId,
                    TenantName = "Alpine Rent",
                    PickupAddress = "ul. Górska 1",
                    PickupCity = "Zakopane",
                    DailyPrice = 100m,
                    Quantity = 2,
                    StartDate = DateTime.Today.AddDays(1).AddHours(9),
                    EndDate = DateTime.Today.AddDays(2).AddHours(9)
                }
            ]
        };
        _cart.Setup(service => service.GetCart()).Returns(cart);
        _api
            .Setup(service => service.GetPaymentQuoteAsync(It.IsAny<PaymentQuoteRequest>()))
            .ReturnsAsync(new PaymentQuoteResponse
            {
                TotalAmount = 246.80m,
                DepositAmount = 61.70m,
                RentalCount = 1,
                Items =
                [
                    new PaymentQuoteItemBreakdown
                    {
                        ProductId = productId,
                        Subtotal = 246.80m
                    }
                ],
                Tenants =
                [
                    new TenantQuoteBreakdown
                    {
                        TenantId = tenantId,
                        TenantName = "Alpine Rent",
                        PickupAddress = "ul. Górska 1",
                        PickupCity = "Zakopane",
                        StartDateUtc = DateTime.UtcNow.AddDays(1),
                        EndDateUtc = DateTime.UtcNow.AddDays(2),
                        TotalAmount = 246.80m,
                        DepositAmount = 61.70m,
                        RentalTerms = new RentalTermsSummary
                        {
                            Title = "Regulamin Alpine Rent",
                            Version = "tenant-test",
                            ContentHash = "abc123",
                            Content = "Treść regulaminu",
                            UsesPlatformDefault = false
                        }
                    }
                ]
            });

        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");

            RenderComponent<MudPopoverProvider>();
            var component = RenderComponent<Checkout>();

            component.WaitForAssertion(() =>
            {
                component.Find(".checkout-item-price").TextContent.Trim()
                    .Should().Be("246,80 zł");
                component.Find(".checkout-summary-total").TextContent
                    .Should().Contain("61,70 zł");
                component.Find(".checkout-item-price").TextContent
                    .Should().NotContain("200");
                component.Find(".checkout-tenant-items-header").TextContent
                    .Should().Contain("Alpine Rent").And.Contain("Odbiór: ul. Górska 1, Zakopane");
                var tenantSummary = component.Find(".checkout-rental-group-summary").TextContent;
                tenantSummary.Should().Contain("Regulamin Alpine Rent");
                tenantSummary.Should().NotContain("Osobna rezerwacja");
            });
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void Checkout_BlocksPaymentAndShowsValidationMessagesForInvalidCustomerData()
    {
        var productId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var cart = new CartModel
        {
            Items =
            [
                new CartItem
                {
                    ProductId = productId,
                    ProductName = "Rower testowy",
                    TenantId = tenantId,
                    TenantName = "Rowerownia",
                    DailyPrice = 80m,
                    Quantity = 1,
                    StartDate = PolishRentalTime.NowLocal.AddDays(1),
                    EndDate = PolishRentalTime.NowLocal.AddDays(2)
                }
            ]
        };
        _cart.Setup(service => service.GetCart()).Returns(cart);
        _api
            .Setup(service => service.GetPaymentQuoteAsync(It.IsAny<PaymentQuoteRequest>()))
            .ReturnsAsync(new PaymentQuoteResponse
            {
                TotalAmount = 80m,
                DepositAmount = 20m,
                RentalCount = 1,
                Items = [new PaymentQuoteItemBreakdown { ProductId = productId, Subtotal = 80m }],
                Tenants =
                [
                    new TenantQuoteBreakdown
                    {
                        TenantId = tenantId,
                        TenantName = "Rowerownia",
                        StartDateUtc = DateTime.UtcNow.AddDays(1),
                        EndDateUtc = DateTime.UtcNow.AddDays(2),
                        TotalAmount = 80m,
                        DepositAmount = 20m,
                        RentalTerms = new RentalTermsSummary
                        {
                            Title = "Regulamin Rowerowni",
                            Version = "tenant-test",
                            ContentHash = "terms-hash",
                            Content = "Treść regulaminu"
                        }
                    }
                ]
            });

        RenderComponent<MudPopoverProvider>();
        var component = RenderComponent<Checkout>();

        component.WaitForElement(".checkout-submit-btn");
        component.Find("#checkout-fullname").Change("Jan Kowalski");
        component.Find("#checkout-email").Change("niepoprawny-email");
        component.Find(".checkout-tenant-legal-ack input").Change(true);
        component.Find(".checkout-legal-ack input").Change(true);

        component.WaitForAssertion(() =>
            component.Find(".checkout-submit-btn").HasAttribute("disabled").Should().BeFalse());
        component.Find(".checkout-submit-btn").Click();

        component.WaitForAssertion(() =>
        {
            component.Markup.Should().Contain("Podaj poprawny adres email.");
            component.Markup.Should().Contain("Telefon jest wymagany.");
        });
        _api.Verify(
            service => service.CreateGuestSessionAsync(It.IsAny<GuestSessionPayload>()),
            Times.Never);
        _api.Verify(
            service => service.CreateCheckoutSessionAsync(It.IsAny<CreateCheckoutSessionRequest>()),
            Times.Never);
    }

    private sealed class AnonymousAuthenticationStateProvider : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));
    }
}
