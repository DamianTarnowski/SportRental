using FluentAssertions;
using SportRental.Client.Services;

namespace SportRental.Client.Tests.Services;

public sealed class SafeClientNavigationTests
{
    [Theory]
    [InlineData("my-rentals", "my-rentals")]
    [InlineData("products?search=narty", "products?search=narty")]
    [InlineData("/products", "./")]
    [InlineData("https://example.test", "./")]
    [InlineData("//example.test", "./")]
    [InlineData("../admin", "./")]
    [InlineData("foo\\bar", "./")]
    public void NormalizeRelativeReturnUrl_AllowsOnlyLocalClientRoutes(string input, string expected)
    {
        SafeClientNavigation.NormalizeRelativeReturnUrl(input).Should().Be(expected);
    }

    [Fact]
    public void ToBundledClientPath_PreservesSafeRequestedRoute()
    {
        SafeClientNavigation.ToBundledClientPath("my-rentals?status=Confirmed")
            .Should().Be("/_client/my-rentals?status=Confirmed");
    }

    [Fact]
    public void RelativeClientRoute_ResolvesInsideBundledBasePath()
    {
        var bundledBase = new Uri("https://app.example.test/_client/");
        var safeRoute = SafeClientNavigation.NormalizeRelativeReturnUrl("products?search=narty");

        new Uri(bundledBase, safeRoute).AbsoluteUri
            .Should().Be("https://app.example.test/_client/products?search=narty");
    }
}
