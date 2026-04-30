using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using SportRental.Admin.Components.Shared;
using SportRental.Infrastructure.Domain;

namespace SportRental.Admin.Tests.Components;

/// <summary>
/// CustomerTrustBadge ma 4 mapowania TrustLevel -> (emoji, label, color, tooltip).
/// To pure presentational, więc bUnit z parametrami wystarczy — bez DbContext/Auth.
/// </summary>
public class CustomerTrustBadgeTests : TestContext
{
    public CustomerTrustBadgeTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private IRenderedFragment RenderWithProviders(Customer customer, bool showMetrics = true)
    {
        // MudTooltip wewnątrz CustomerTrustBadge potrzebuje MudPopoverProvider w drzewie.
        return Render(builder =>
        {
            builder.OpenComponent<MudPopoverProvider>(0);
            builder.CloseComponent();
            builder.OpenComponent<CustomerTrustBadge>(1);
            builder.AddAttribute(2, nameof(CustomerTrustBadge.Customer), customer);
            builder.AddAttribute(3, nameof(CustomerTrustBadge.ShowMetrics), showMetrics);
            builder.CloseComponent();
        });
    }

    [Theory]
    [InlineData(CustomerTrustLevel.Unverified, "Zweryfikowany")]
    [InlineData(CustomerTrustLevel.Good, "Bez szkód")]
    [InlineData(CustomerTrustLevel.Watch, "Wymaga uwagi")]
    [InlineData(CustomerTrustLevel.Restricted, "Ograniczone")]
    public void Badge_RendersCorrectLabel_ForEachTrustLevel(CustomerTrustLevel level, string expectedLabel)
    {
        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            FullName = "Test",
            TrustLevel = level
        };

        var cut = RenderWithProviders(customer);

        cut.Markup.Should().Contain(expectedLabel);
    }

    [Theory]
    [InlineData(CustomerTrustLevel.Good, "🟢")]
    [InlineData(CustomerTrustLevel.Watch, "🟡")]
    [InlineData(CustomerTrustLevel.Restricted, "🔴")]
    [InlineData(CustomerTrustLevel.Unverified, "✅")]
    public void Badge_RendersCorrectEmoji_ForEachLevel(CustomerTrustLevel level, string expectedEmoji)
    {
        var customer = new Customer { Id = Guid.NewGuid(), FullName = "Test", TrustLevel = level };

        var cut = RenderWithProviders(customer);

        cut.Markup.Should().Contain(expectedEmoji);
    }

    [Fact]
    public void Badge_WithMetricsEnabled_ShowsRentalCountAndAverage()
    {
        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            FullName = "Test",
            TrustLevel = CustomerTrustLevel.Good,
            TrustCompletedRentalsCount = 12,
            TrustAverageScore = 9.4
        };

        var cut = RenderWithProviders(customer, showMetrics: true);

        cut.Markup.Should().Contain("12 ocen");
        // Polish locale → przecinek dziesiętny ("9,4"), invariant fallback → kropka ("9.4").
        (cut.Markup.Contains("9,4/10") || cut.Markup.Contains("9.4/10"))
            .Should().BeTrue("badge powinien pokazać średnią 9,4/10 lub 9.4/10");
    }

    [Fact]
    public void Badge_WithMetricsDisabled_HidesNumbers()
    {
        var customer = new Customer
        {
            Id = Guid.NewGuid(), FullName = "Test",
            TrustLevel = CustomerTrustLevel.Good,
            TrustCompletedRentalsCount = 12,
            TrustAverageScore = 9.4
        };

        var cut = RenderWithProviders(customer, showMetrics: false);

        cut.Markup.Should().NotContain("12 ocen");
        cut.Markup.Should().NotContain("9,4/10");
        cut.Markup.Should().NotContain("9.4/10");
    }

    [Fact]
    public void Badge_SingleReview_UsesSingularForm()
    {
        var customer = new Customer
        {
            Id = Guid.NewGuid(), FullName = "Test",
            TrustLevel = CustomerTrustLevel.Unverified,
            TrustCompletedRentalsCount = 1,
            TrustAverageScore = 9.0
        };

        var cut = RenderWithProviders(customer, showMetrics: true);

        cut.Markup.Should().Contain("1 ocena");
        cut.Markup.Should().NotContain("1 ocen ");
    }

    [Fact]
    public void Badge_ZeroReviews_HidesMetricsBlock()
    {
        var customer = new Customer
        {
            Id = Guid.NewGuid(), FullName = "Test",
            TrustLevel = CustomerTrustLevel.Unverified,
            TrustCompletedRentalsCount = 0
        };

        var cut = RenderWithProviders(customer, showMetrics: true);

        // Brak ocen → nie powinno być sekcji metryk z "ocen"/"ocena", ale label "Zweryfikowany"
        // (Unverified) wciąż pokazany.
        cut.Markup.Should().Contain("Zweryfikowany");
        cut.Markup.Should().NotContain("ocen ·");  // separator metryk
    }

    [Fact]
    public void Badge_RestrictedLevel_UsesErrorColor()
    {
        var customer = new Customer
        {
            Id = Guid.NewGuid(), FullName = "Test",
            TrustLevel = CustomerTrustLevel.Restricted
        };

        var cut = RenderWithProviders(customer);

        // MudChip z Color.Error renderuje klasę CSS mud-chip-color-error
        cut.Markup.Should().Contain("mud-chip-color-error");
    }

    [Fact]
    public void Badge_GoodLevel_UsesSuccessColor()
    {
        var customer = new Customer
        {
            Id = Guid.NewGuid(), FullName = "Test",
            TrustLevel = CustomerTrustLevel.Good
        };

        var cut = RenderWithProviders(customer);

        cut.Markup.Should().Contain("mud-chip-color-success");
    }

    [Fact]
    public void Badge_WatchLevel_UsesWarningColor()
    {
        var customer = new Customer
        {
            Id = Guid.NewGuid(), FullName = "Test",
            TrustLevel = CustomerTrustLevel.Watch
        };

        var cut = RenderWithProviders(customer);

        cut.Markup.Should().Contain("mud-chip-color-warning");
    }
}
