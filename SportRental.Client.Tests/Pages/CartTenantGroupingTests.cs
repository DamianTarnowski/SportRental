using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using MudBlazor.Services;
using SportRental.Shared.Models;
using SportRental.Shared.Services;
using CartPage = SportRental.Client.Pages.Cart;

namespace SportRental.Client.Tests.Pages;

public sealed class CartTenantGroupingTests : TestContext
{
    [Fact]
    public void Cart_GroupsItemsByTenantAndExplainsSeparatePickupsOnDesktopAndMobile()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var productA1 = Guid.NewGuid();
        var productA2 = Guid.NewGuid();
        var productB = Guid.NewGuid();
        var start = DateTime.Today.AddDays(2).AddHours(9);
        var end = start.AddDays(2);
        var cart = new Cart
        {
            Items =
            [
                CreateItem(productA1, tenantA, "Alpine Rent", "ul. Górska 12", "Zakopane", "Narty", start, end),
                CreateItem(productA2, tenantA, "Alpine Rent", "ul. Górska 12", "Zakopane", "Kask", start, end),
                CreateItem(
                    productB,
                    tenantB,
                    "Bike Point",
                    "ul. Wiślna 7",
                    "Kraków",
                    "Rower",
                    start,
                    end,
                    RentalTypeDto.Hourly,
                    15m,
                    4)
            ]
        };

        var cartService = new Mock<ICartService>();
        cartService.Setup(service => service.GetCart()).Returns(cart);
        cartService.Setup(service => service.RefreshHoldsIfNeededAsync(It.IsAny<TimeSpan?>()))
            .Returns(Task.CompletedTask);

        var api = new Mock<IApiService>();
        api.Setup(service => service.GetPaymentQuoteAsync(It.IsAny<PaymentQuoteRequest>()))
            .ReturnsAsync(new PaymentQuoteResponse
            {
                TotalAmount = 420m,
                DepositAmount = 126m,
                RentalCount = 2,
                Items =
                [
                    new PaymentQuoteItemBreakdown { ProductId = productA1, Subtotal = 200m },
                    new PaymentQuoteItemBreakdown { ProductId = productA2, Subtotal = 40m },
                    new PaymentQuoteItemBreakdown { ProductId = productB, Subtotal = 180m }
                ]
            });

        Services.AddSingleton(cartService.Object);
        Services.AddSingleton(api.Object);
        RenderComponent<MudBlazor.MudPopoverProvider>();

        var component = RenderComponent<CartPage>();

        component.WaitForAssertion(() =>
        {
            var groups = component.FindAll(".cart-tenant-group");
            groups.Should().HaveCount(2);
            groups.Single(group => group.TextContent.Contains("Alpine Rent", StringComparison.Ordinal))
                .TextContent.Should().Contain("Odbiór: ul. Górska 12, Zakopane").And.Contain("Narty").And.Contain("Kask");
            groups.Single(group => group.TextContent.Contains("Bike Point", StringComparison.Ordinal))
                .TextContent.Should().Contain("Odbiór: ul. Wiślna 7, Kraków").And.Contain("Rower");
            groups.Should().OnlyContain(group => group.QuerySelectorAll("input[type=datetime-local]").Length == 2);
            component.Find(".cart-multi-tenant-notice").TextContent
                .Should().Contain("osobną rezerwację").And.Contain("jedną płatnością");
            component.Markup.Should().Contain("Przejdź do podsumowania");
        });

        component.Instance.OnScreenResize(true);

        component.WaitForAssertion(() =>
        {
            component.FindAll(".cart-mobile-tenant-group").Should().HaveCount(2);
            component.FindAll(".cart-mobile-tenant-group")
                .Should().OnlyContain(group => group.QuerySelectorAll("input[type=datetime-local]").Length == 2);
            component.Markup.Should().Contain("2 punkty odbioru");
            component.Markup.Should().Contain("Alpine Rent");
            component.Markup.Should().Contain("Bike Point");
            component.Markup.Should().Contain("Typ wynajmu");
            component.FindAll(".cart-mobile-tenant-group")
                .Single(group => group.TextContent.Contains("Bike Point", StringComparison.Ordinal))
                .TextContent.Should().Contain("15,00 zł/godz.").And.NotContain("70,00 zł/dzień");
        });
    }

    private static CartItem CreateItem(
        Guid productId,
        Guid tenantId,
        string tenantName,
        string pickupAddress,
        string pickupCity,
        string productName,
        DateTime start,
        DateTime end,
        RentalTypeDto rentalType = RentalTypeDto.Daily,
        decimal? hourlyPrice = null,
        int? hoursRented = null) => new()
    {
        ProductId = productId,
        ProductName = productName,
        TenantId = tenantId,
        TenantName = tenantName,
        PickupAddress = pickupAddress,
        PickupCity = pickupCity,
        DailyPrice = 70m,
        Quantity = 1,
        StartDate = start,
        EndDate = end,
        RentalType = rentalType,
        HourlyPrice = hourlyPrice,
        HoursRented = hoursRented,
        HoldId = Guid.NewGuid(),
        HoldExpiresAtUtc = DateTime.UtcNow.AddMinutes(10)
    };
}
