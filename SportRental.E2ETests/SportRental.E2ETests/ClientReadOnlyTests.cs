using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace SportRental.E2ETests;

[TestFixture]
[Category("ClientReadOnly")]
[NonParallelizable]
public sealed class ClientReadOnlyTests : ClientReadOnlyTestBase
{
    private static readonly Regex ProductDetailsPath = new(
        @"/_client/products/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}(?:[?#]|$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly (string Route, string Name)[] PublicRoutes =
    [
        ("/", "home"),
        ("/products", "products"),
        ("/map", "map"),
        ("/reviews", "reviews"),
        ("/contact", "contact"),
        ("/terms", "terms"),
        ("/privacy", "privacy"),
        ("/select-tenant", "select-tenant"),
        ("/login", "login"),
        ("/register", "register"),
        ("/guest-access", "guest-access"),
        ("/cart", "cart")
    ];

    [Test]
    public async Task WasmShell_StartsWithoutCriticalBrowserErrors()
    {
        await OpenClientRouteAsync();

        await Expect(Page.Locator(".sr-hero-title")).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Link, new PageGetByRoleOptions
        {
            NameRegex = new Regex("RentSpot", RegexOptions.IgnoreCase)
        }).First).ToBeVisibleAsync();

        await AssertWasmBootedAsync();
        await AssertSameOriginLinksStayInClientAsync("home");
        await AssertNoHorizontalOverflowAsync("home desktop");
        await CaptureScreenshotAsync("home-desktop");
        AssertNoCriticalDiagnostics();
    }

    [Test]
    public async Task Catalog_HasRealEquipmentRentalAndPickupData_AndDetailsOpenReadOnly()
    {
        await OpenClientRouteAsync("/products");

        var cards = Page.Locator(".product-card");
        await Expect(cards.First).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 45_000 });

        var cardCount = await cards.CountAsync();
        Assert.That(cardCount, Is.GreaterThan(0), "Katalog nie zawiera żadnych kart sprzętu.");

        var cardsWithPickup = cards.Filter(new LocatorFilterOptions
        {
            Has = Page.Locator(".product-card-tenant-city")
        });
        var cardsWithPickupCount = await cardsWithPickup.CountAsync();
        Assert.That(cardsWithPickupCount, Is.GreaterThan(0),
            "Katalog nie pokazuje na żadnej karcie miasta ani adresu odbioru (City/PickupAddress).");

        var productCard = cardsWithPickup.First;
        var productName = (await productCard.Locator(".product-card-title").InnerTextAsync()).Trim();
        var rentalName = (await productCard.Locator(".product-card-tenant-name").InnerTextAsync()).Trim();
        var pickupText = (await productCard.Locator(".product-card-tenant-city").InnerTextAsync()).Trim();
        var priceText = (await productCard.Locator(".product-price-amount").InnerTextAsync()).Trim();

        Assert.Multiple(() =>
        {
            Assert.That(productName, Is.Not.Empty, "Pierwsza karta nie ma nazwy sprzętu.");
            Assert.That(rentalName, Is.Not.Empty.And.Not.EqualTo("Wypożyczalnia"),
                "Pierwsza karta nie ma rzeczywistej nazwy wypożyczalni.");
            Assert.That(pickupText, Does.Match(@"^Odbiór:\s*\S+"),
                "Pierwsza karta nie ma informacji o miejscu odbioru.");
            Assert.That(priceText, Does.Match(@"\d"), "Pierwsza karta nie ma ceny.");
        });

        ReportObservation(
            $"Catalog cards: {cardCount}; first product: {productName}; rental: {rentalName}; pickup: {pickupText}");
        await AssertNoHorizontalOverflowAsync("catalog desktop");
        await CaptureScreenshotAsync("catalog-desktop");

        await OpenProductDetailsRouteAsync(productCard, isMobile: false);
        await WaitForLayoutToSettleAsync();
        AssertClientLocation("first product details");

        var details = Page.Locator(".product-details-info");
        await Expect(details.Locator(".product-details-title")).ToBeVisibleAsync();
        await Expect(details.Locator(".product-details-title")).ToHaveTextAsync(productName);
        await Expect(details.GetByText(new Regex("^Odbiór:", RegexOptions.IgnoreCase)).First).ToBeVisibleAsync();

        // Intentionally do not click .product-details-add-btn: this suite is read-only.
        await AssertSameOriginLinksStayInClientAsync("product details");
        await AssertNoHorizontalOverflowAsync("product details desktop");
        await CaptureScreenshotAsync("product-details-desktop");
        AssertNoCriticalDiagnostics();
    }

    [Test]
    public async Task PublicRoutes_AndSameOriginLinks_RemainUnderClientBasePath()
    {
        foreach (var (route, name) in PublicRoutes)
        {
            await OpenClientRouteAsync(route);

            var title = await Page.TitleAsync();
            Assert.That(title.StartsWith("404", StringComparison.OrdinalIgnoreCase), Is.False,
                $"Publiczna trasa {route} trafiła do widoku 404.");

            await AssertSameOriginLinksStayInClientAsync(name);
            ReportObservation($"Public route OK: {route} -> {new Uri(Page.Url).AbsolutePath}");
        }

        await CaptureScreenshotAsync("public-routes-final-cart");
        AssertNoCriticalDiagnostics();
    }

    [TestCase(1440, 1000, "desktop", TestName = "ResponsiveCoreJourney_Desktop_HasNoHorizontalOverflow")]
    [TestCase(390, 844, "mobile", TestName = "ResponsiveCoreJourney_Mobile_HasNoHorizontalOverflow")]
    public async Task ResponsiveCoreJourney_HasNoHorizontalOverflow(int width, int height, string profile)
    {
        await Page.SetViewportSizeAsync(width, height);

        await OpenClientRouteAsync();
        await AssertNoHorizontalOverflowAsync($"home {profile}");
        await CaptureScreenshotAsync($"home-{profile}");

        await OpenClientRouteAsync("/products");
        var cardSelector = profile == "mobile" ? ".product-mobile-card" : ".product-card";
        var detailsSelector = profile == "mobile" ? ".product-mobile-title" : ".product-details-title";
        var cards = Page.Locator(cardSelector);
        await Expect(cards.First).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 45_000 });
        await AssertNoHorizontalOverflowAsync($"catalog {profile}");
        await CaptureScreenshotAsync($"catalog-{profile}");

        await OpenProductDetailsRouteAsync(cards.First, profile == "mobile");
        await Expect(Page.Locator(detailsSelector)).ToBeVisibleAsync();
        await WaitForLayoutToSettleAsync();
        AssertClientLocation($"details {profile}");
        await AssertNoHorizontalOverflowAsync($"product details {profile}");
        await CaptureScreenshotAsync($"product-details-{profile}");

        AssertNoCriticalDiagnostics();
    }

    [Test]
    public async Task LegacyProductsRoute_RedirectsToBundledClient_AndPreservesQuery()
    {
        var legacyUrl = new Uri(AdminRoot, "/products?e2e=legacy-readonly").AbsoluteUri;
        ReportObservation("Navigate legacy route: /products?e2e=<redacted>");

        var response = await Page.GotoAsync(legacyUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60_000
        });

        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Status, Is.InRange(200, 399));
        await Expect(Page.Locator(".sr-header")).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions
        {
            Timeout = 60_000
        });
        await Expect(Page.Locator(".product-card").First).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions
        {
            Timeout = 45_000
        });

        var finalUrl = new Uri(Page.Url);
        Assert.Multiple(() =>
        {
            Assert.That(finalUrl.AbsolutePath, Is.EqualTo("/_client/products").IgnoreCase,
                "Legacy /products nie przekierował do katalogu bundlowanego Client WASM.");
            Assert.That(finalUrl.Query, Does.Contain("e2e=legacy-readonly"),
                "Przekierowanie legacy zgubiło query string.");
        });

        await AssertNoHorizontalOverflowAsync("legacy products redirect");
        await CaptureScreenshotAsync("legacy-products-redirect");
        AssertNoCriticalDiagnostics();
    }

    private async Task OpenProductDetailsRouteAsync(ILocator card, bool isMobile)
    {
        ILocator detailsButton;
        if (isMobile)
        {
            detailsButton = card.Locator(".product-mobile-details-btn");
        }
        else
        {
            await card.HoverAsync();
            detailsButton = card.Locator(".product-quick-btn-secondary");
        }

        await Expect(detailsButton).ToBeVisibleAsync();

        // Navigation is handled by the Blazor router and does not produce a full document
        // Load event. Observe the SPA URL and rendered view instead of a load state.
        await detailsButton.ClickAsync();
        await Expect(Page).ToHaveURLAsync(ProductDetailsPath);
        await Expect(Page.Locator(isMobile ? ".product-mobile-title" : ".product-details-title"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });
    }
}
