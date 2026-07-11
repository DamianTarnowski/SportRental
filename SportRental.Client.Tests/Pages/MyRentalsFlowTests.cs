using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using MudBlazor.Services;
using SportRental.Client.Pages;
using SportRental.Shared.Models;
using SportRental.Shared.Services;

namespace SportRental.Client.Tests.Pages;

public sealed class MyRentalsFlowTests : TestContext
{
    private readonly Mock<IApiService> _api = new();

    public MyRentalsFlowTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton(_api.Object);
    }

    [Fact]
    public void MyRentals_ShowsHourlyTenantPickupPaymentAndItems()
    {
        var rental = CreateHourlyRental();
        _api.Setup(GetMyRentalsCall()).ReturnsAsync(new List<MyRentalDto> { rental });

        var component = RenderComponent<MyRentals>();

        component.WaitForAssertion(() =>
        {
            var text = component.Find(".rental-card").TextContent;
            text.Should().Contain("Rent Spot Kraków");
            text.Should().Contain("Punkt odbioru: Długa 1, Kraków");
            text.Should().Contain("Wynajem godzinowy");
            text.Should().Contain("4 godziny");
            text.Should().Contain("Depozyt opłacony");
            text.Should().Contain("Narty PRO × 2");
            text.Should().Contain("Opłacono za wynajem:");
            text.Should().Contain("Depozyt pobrany:");
            text.Should().Contain("125");
            text.Should().Contain("Do zapłaty za wynajem:");
            text.Should().Contain("500");
        });
    }

    [Fact]
    public void MyRentals_MobileViewKeepsTheSameFunctionalRentalData()
    {
        var rental = CreateHourlyRental();
        _api.Setup(GetMyRentalsCall()).ReturnsAsync(new List<MyRentalDto> { rental });
        var component = RenderComponent<MyRentals>();
        component.WaitForAssertion(() => component.FindAll(".rental-card").Should().ContainSingle());

        component.Instance.OnScreenResize(true);

        component.WaitForAssertion(() =>
        {
            var text = component.Find(".rental-mobile-card").TextContent;
            text.Should().Contain("Rent Spot Kraków");
            text.Should().Contain("Długa 1, Kraków");
            text.Should().Contain("Wynajem godzinowy");
            text.Should().Contain("4 godziny");
            text.Should().Contain("Depozyt opłacony");
            text.Should().Contain("Narty PRO × 2");
            text.Should().Contain("Opłacono za wynajem:");
            text.Should().Contain("Depozyt pobrany:");
            text.Should().Contain("Do zapłaty za wynajem:");
        });
    }

    [Fact]
    public void MyRentals_ShowsLoadFailureInsteadOfFalseEmptyState()
    {
        _api.Setup(GetMyRentalsCall()).ThrowsAsync(new HttpRequestException("offline"));

        var component = RenderComponent<MyRentals>();

        component.WaitForAssertion(() =>
        {
            component.Markup.Should().Contain("Nie udało się pobrać wypożyczeń");
            component.Markup.Should().Contain("Spróbuj ponownie");
            component.Markup.Should().NotContain("Nie masz jeszcze żadnych wypożyczeń");
        });
    }

    [Fact]
    public void RentalDetails_UsesDtoRentalTypeContactAndCanCancelFlag()
    {
        var rental = CreateHourlyRental();
        rental.CanCancel = false;
        rental.Status = "Confirmed";
        _api.Setup(GetMyRentalsCall()).ReturnsAsync(new List<MyRentalDto> { rental });

        var component = RenderComponent<RentalDetails>(parameters => parameters.Add(p => p.Id, rental.Id));

        component.WaitForAssertion(() =>
        {
            var text = component.Markup;
            text.Should().Contain("Rent Spot Kraków");
            text.Should().Contain("Długa 1, Kraków");
            text.Should().Contain("500 600 700");
            text.Should().Contain("kontakt@rent.example");
            text.Should().Contain("Wynajem godzinowy");
            text.Should().Contain("4 godziny");
            text.Should().Contain("Cena za godzinę");
            text.Should().Contain("Depozyt opłacony");
            text.Should().NotContain("Anuluj wypożyczenie");
            text.Should().NotContain("Wystaw opinię");
        });
    }

    [Fact]
    public void RentalDetails_ShowsCancellationOnlyWhenDtoAllowsIt()
    {
        var rental = CreateHourlyRental();
        rental.Status = "Pending";
        rental.CanCancel = true;
        _api.Setup(GetMyRentalsCall()).ReturnsAsync(new List<MyRentalDto> { rental });

        var component = RenderComponent<RentalDetails>(parameters => parameters.Add(p => p.Id, rental.Id));

        component.WaitForAssertion(() => component.Markup.Should().Contain("Anuluj wypożyczenie"));
    }

    [Fact]
    public void RentalDetails_ShowsDailyDurationAndDailyItemPrice()
    {
        var rental = CreateHourlyRental();
        rental.RentalType = RentalTypeDto.Daily;
        rental.HoursRented = null;
        rental.StartDateUtc = new DateTime(2026, 7, 14, 8, 0, 0, DateTimeKind.Utc);
        rental.EndDateUtc = new DateTime(2026, 7, 15, 20, 0, 0, DateTimeKind.Utc);
        _api.Setup(GetMyRentalsCall()).ReturnsAsync(new List<MyRentalDto> { rental });

        var component = RenderComponent<RentalDetails>(parameters => parameters.Add(p => p.Id, rental.Id));

        component.WaitForAssertion(() =>
        {
            component.Markup.Should().Contain("Wynajem dzienny");
            component.Markup.Should().Contain("2 dni");
            component.Markup.Should().Contain("Cena za dzień");
            component.Markup.Should().NotContain("Cena za godzinę");
        });
    }

    [Fact]
    public void ReviewAction_IsAvailableOnlyForCompletedRentalWithoutReview()
    {
        var completed = CreateHourlyRental();
        completed.Status = "Completed";
        completed.HasReview = false;

        var active = CreateHourlyRental();
        active.Id = Guid.NewGuid();
        active.Status = "Active";
        active.HasReview = false;

        _api.Setup(GetMyRentalsCall()).ReturnsAsync(new List<MyRentalDto> { completed, active });

        var component = RenderComponent<MyRentals>();

        component.WaitForAssertion(() =>
        {
            component.FindAll("button")
                .Count(button => button.TextContent.Contains("Oceń", StringComparison.Ordinal))
                .Should().Be(1);
        });
    }

    [Fact]
    public void MyRentals_SubtractsDepositRetainedForDamageFromOutstandingCharge()
    {
        var rental = CreateHourlyRental();
        rental.Status = "Completed";
        rental.PaidAmount = rental.TotalAmount;
        rental.DepositAmount = 300m;
        rental.DepositPaidAtUtc = DateTime.UtcNow.AddDays(-1);
        rental.ReturnDepositRefund = 200m;
        rental.DamageCharge = 100m;
        _api.Setup(GetMyRentalsCall()).ReturnsAsync(new List<MyRentalDto> { rental });

        var component = RenderComponent<MyRentals>();

        component.WaitForAssertion(() =>
        {
            var text = component.Find(".rental-card").TextContent;
            text.Should().Contain("Do zapłaty za wynajem: 0,00 zł");
            text.Should().Contain("Depozyt: zwrócono 200,00 zł, zatrzymano 100,00 zł");
        });
    }

    [Fact]
    public void MyRentals_GroupsMarketplaceRentalsAndShowsAggregateDepositRefund()
    {
        var orderId = Guid.NewGuid();
        var first = CreateHourlyRental();
        first.MarketplaceOrderId = orderId;
        first.MarketplaceOrderNumber = "RS-20260710-ABC12345";
        first.OrderSequence = 1;
        first.OrderRentalCount = 2;
        first.ReturnDepositRefund = 75m;
        first.DepositAmount = 125m;
        first.Status = "Completed";

        var second = CreateHourlyRental();
        second.Id = Guid.NewGuid();
        second.TenantId = Guid.NewGuid();
        second.TenantName = "Bike Point";
        second.MarketplaceOrderId = orderId;
        second.MarketplaceOrderNumber = first.MarketplaceOrderNumber;
        second.OrderSequence = 2;
        second.OrderRentalCount = 2;
        second.DepositAmount = 50m;
        second.PaymentStatus = "DepositRefunded";
        second.Status = "Cancelled";

        _api.Setup(GetMyRentalsCall()).ReturnsAsync(new List<MyRentalDto> { second, first });

        var component = RenderComponent<MyRentals>();

        component.WaitForAssertion(() =>
        {
            var order = component.Find(".rental-order-header").TextContent;
            order.Should().Contain("RS-20260710-ABC12345");
            order.Should().Contain("2 osobne rezerwacje");
            order.Should().Contain("Depozyt początkowy 175,00 zł");
            order.Should().Contain("Zwrócono 125,00 zł");
            component.FindAll(".rental-card").Should().HaveCount(2);
        });
    }

    private static System.Linq.Expressions.Expression<Func<IApiService, Task<List<MyRentalDto>>>> GetMyRentalsCall() =>
        api => api.GetMyRentalsAsync(
            It.IsAny<string?>(),
            It.IsAny<DateTime?>(),
            It.IsAny<DateTime?>(),
            It.IsAny<Guid?>());

    private static MyRentalDto CreateHourlyRental() => new()
    {
        Id = Guid.NewGuid(),
        TenantId = Guid.NewGuid(),
        TenantName = "Rent Spot Kraków",
        PickupAddress = "Długa 1",
        PickupCity = "Kraków",
        TenantPhoneNumber = "500 600 700",
        TenantEmail = "kontakt@rent.example",
        OpeningHours = "Pon.–Pt. 09:00–18:00",
        CustomerName = "Jan Kowalski",
        StartDateUtc = new DateTime(2026, 7, 14, 8, 0, 0, DateTimeKind.Utc),
        EndDateUtc = new DateTime(2026, 7, 14, 12, 0, 0, DateTimeKind.Utc),
        RentalType = RentalTypeDto.Hourly,
        HoursRented = 4,
        TotalAmount = 500m,
        DepositAmount = 125m,
        PaymentStatus = "DepositPaid",
        PaymentMethod = "Online",
        PaidAtUtc = new DateTime(2026, 7, 10, 10, 0, 0, DateTimeKind.Utc),
        Status = "Confirmed",
        Items =
        [
            new MyRentalItemDto
            {
                RentalItemId = Guid.NewGuid(),
                ProductId = Guid.NewGuid(),
                ProductName = "Narty PRO",
                Quantity = 2,
                DailyPrice = 100m,
                HourlyPrice = 25m,
                TotalPrice = 200m
            }
        ]
    };
}
