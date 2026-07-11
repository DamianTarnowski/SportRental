using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using MudBlazor.Services;
using SportRental.Client.Pages;
using SportRental.Shared.Models;
using SportRental.Shared.Services;

namespace SportRental.Client.Tests.Pages;

public sealed class MapLocationTests : TestContext
{
    [Fact]
    public void CatalogMap_ShowsOnlyLocationsWithPublishedProducts()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();

        var apiService = new Mock<IApiService>();
        apiService.Setup(service => service.GetTenantLocationsAsync())
            .ReturnsAsync(
            [
                new TenantLocationDto
                {
                    TenantId = Guid.NewGuid(),
                    TenantName = "Aktywna wypożyczalnia",
                    Lat = 52.1,
                    Lon = 21.0,
                    ProductCount = 3
                },
                new TenantLocationDto
                {
                    TenantId = Guid.NewGuid(),
                    TenantName = "Pusty punkt kontaktowy",
                    Lat = 51.1,
                    Lon = 19.0,
                    ProductCount = 0
                }
            ]);
        Services.AddSingleton(apiService.Object);

        var component = RenderComponent<Map>();

        component.WaitForAssertion(() =>
        {
            component.Markup.Should().Contain("Aktywna wypożyczalnia");
            component.Markup.Should().Contain("3 produkty w katalogu");
            component.Markup.Should().NotContain("Pusty punkt kontaktowy");
            component.Find("section.map-page[aria-labelledby='map-page-title']").Should().NotBeNull();
            component.FindAll("main").Should().BeEmpty("the shared layout owns the single main landmark");
            component.Find("h1#map-page-title").TextContent.Should().Be("Mapa wypożyczalni");
        });
    }

    [Fact]
    public void SelectLocation_FocusesMarkerAndOpensItsPopup()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var tenantId = Guid.NewGuid();

        var apiService = new Mock<IApiService>();
        apiService.Setup(service => service.GetTenantLocationsAsync())
            .ReturnsAsync(
            [
                new TenantLocationDto
                {
                    TenantId = tenantId,
                    TenantName = "Wypożyczalnia z markerem",
                    Lat = 52.1,
                    Lon = 21.0,
                    ProductCount = 1
                }
            ]);
        Services.AddSingleton(apiService.Object);

        var component = RenderComponent<Map>();
        component.WaitForElement("button");
        component.FindAll("button")
            .Single(button => button.TextContent.Contains("Pokaż na mapie", StringComparison.Ordinal))
            .Click();

        component.WaitForAssertion(() =>
            JSInterop.Invocations.Should().Contain(invocation =>
                invocation.Identifier == "leafletMap.focusShopMarker" &&
                invocation.Arguments.Contains(tenantId)));
    }

    [Fact]
    public async Task ClearLocation_RemovesPersistedJavaScriptLocation()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();

        var apiService = new Mock<IApiService>();
        apiService.Setup(service => service.GetTenantLocationsAsync())
            .ReturnsAsync([]);
        Services.AddSingleton(apiService.Object);

        var component = RenderComponent<Map>();
        await component.InvokeAsync(() => component.Instance.OnUserLocationChanged(50.1, 19.9));

        component.FindAll("button")
            .Single(button => button.TextContent.Contains("Wyczyść", StringComparison.Ordinal))
            .Click();

        JSInterop.Invocations.Should().Contain(invocation =>
            invocation.Identifier == "leafletMap.clearUserLocation");
    }
}
