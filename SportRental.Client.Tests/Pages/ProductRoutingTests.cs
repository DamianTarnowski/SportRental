using Blazored.LocalStorage;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using MudBlazor;
using MudBlazor.Services;
using System.Net.Http.Json;
using SportRental.Client.Pages;
using SportRental.Client.Services;
using SportRental.Shared.Models;
using SportRental.Shared.Services;

namespace SportRental.Client.Tests.Pages;

public sealed class ProductRoutingTests : TestContext
{
    private readonly Mock<IApiService> _apiService = new();
    private readonly Mock<ICartService> _cartService = new();
    private readonly Mock<ILocalStorageService> _localStorage = new();
    private readonly Mock<ISnackbar> _snackbar = new();
    private readonly TenantListHandler _tenantListHandler = new();
    private readonly SportRental.Shared.Models.Cart _cart = new();

    public ProductRoutingTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton(_snackbar.Object);
        Services.AddSingleton(_apiService.Object);
        Services.AddSingleton(_cartService.Object);
        Services.AddSingleton(new TenantService(
            new HttpClient(_tenantListHandler) { BaseAddress = new Uri("http://localhost") },
            _localStorage.Object));

        _apiService
            .Setup(x => x.GetProductsPagedAsync(It.IsAny<ProductFilterRequest>()))
            .ReturnsAsync((ProductFilterRequest filter) => new ProductsPagedResponse
            {
                Page = filter.Page,
                PageSize = filter.PageSize,
                TotalPages = 1
            });
        _apiService
            .Setup(x => x.GetProductCatalogFacetsAsync())
            .ReturnsAsync(new ProductCatalogFacetsDto());
        _cartService.Setup(service => service.GetCart()).Returns(_cart);
    }

    [Fact]
    public void Products_AppliesSearchCategoryAndTenantIdFromRouteParameters()
    {
        var tenantId = Guid.NewGuid();
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo(
            $"products?search=%20%20narty%20%20&category=%20%20Zimowe%20%20&tenantId=%20%20{tenantId:D}%20%20");

        RenderComponent<MudPopoverProvider>();
        RenderComponent<Products>();

        _apiService.Verify(x => x.GetProductsPagedAsync(
            It.Is<ProductFilterRequest>(filter =>
                filter.PageSize == 12 &&
                filter.Search == "narty" &&
                filter.Category == "Zimowe" &&
                filter.TenantId == tenantId &&
                filter.Tenant == null)), Times.Once);
    }

    [Fact]
    public void Products_DoesNotSilentlyRestrictMarketplaceToSavedTenant()
    {
        var tenantId = Guid.NewGuid();
        _localStorage
            .Setup(storage => storage.GetItemAsync<string>("selected_tenant_id", default))
            .ReturnsAsync(tenantId.ToString("D"));

        RenderComponent<MudPopoverProvider>();
        RenderComponent<Products>();

        _apiService.Verify(service => service.GetProductsPagedAsync(
            It.Is<ProductFilterRequest>(filter =>
                filter.PageSize == 12 && filter.TenantId == null)), Times.Once);
    }

    [Fact]
    public void Products_AddToCartUsesDaytimeWindow()
    {
        var product = new ProductDto
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            TenantName = "Rent Spot Kraków",
            Name = "Narty testowe",
            DailyPrice = 100m,
            IsAvailable = true,
            AvailableQuantity = 2
        };
        _apiService
            .Setup(service => service.GetProductsPagedAsync(It.IsAny<ProductFilterRequest>()))
            .ReturnsAsync((ProductFilterRequest filter) => new ProductsPagedResponse
            {
                Items = [product],
                TotalCount = 1,
                Page = filter.Page,
                PageSize = filter.PageSize,
                TotalPages = 1
            });

        DateTime? capturedStart = null;
        DateTime? capturedEnd = null;
        _cartService
            .Setup(service => service.AddToCartAsync(
                product, 1, It.IsAny<DateTime?>(), It.IsAny<DateTime?>()))
            .Callback<ProductDto, int, DateTime?, DateTime?>((_, _, start, end) =>
            {
                capturedStart = start;
                capturedEnd = end;
                _cart.AddItem(product, 1, start, end);
                _cart.Items.Single().HoldId = Guid.NewGuid();
            })
            .Returns(Task.CompletedTask);
        _cartService.Setup(service => service.EnsureHoldsAsync()).ReturnsAsync(true);

        RenderComponent<MudPopoverProvider>();
        var component = RenderComponent<Products>();
        component.Find("button.product-add-btn").Click();

        capturedStart.Should().NotBeNull();
        capturedEnd.Should().NotBeNull();
        capturedStart!.Value.TimeOfDay.Should().Be(TimeSpan.FromHours(9));
        capturedEnd!.Value.TimeOfDay.Should().Be(TimeSpan.FromHours(17));
        capturedStart.Value.Date.Should().Be(DateTime.Today.AddDays(1));
        capturedEnd.Value.Date.Should().Be(DateTime.Today.AddDays(2));
    }

    [Fact]
    public void Products_ShowsPickupContextAndReportsHoldFailureWithoutFalseSuccess()
    {
        var product = new ProductDto
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            TenantName = "Rent Spot Kraków",
            City = "Kraków",
            Name = "Rower testowy",
            DailyPrice = 80m,
            IsAvailable = true,
            AvailableQuantity = 2
        };
        _apiService
            .Setup(service => service.GetProductsPagedAsync(It.IsAny<ProductFilterRequest>()))
            .ReturnsAsync((ProductFilterRequest filter) => new ProductsPagedResponse
            {
                Items = [product],
                TotalCount = 1,
                Page = filter.Page,
                PageSize = filter.PageSize,
                TotalPages = 1
            });
        _cartService
            .Setup(service => service.AddToCartAsync(
                product, 1, It.IsAny<DateTime?>(), It.IsAny<DateTime?>()))
            .Callback<ProductDto, int, DateTime?, DateTime?>((_, _, start, end) =>
                _cart.AddItem(product, 1, start, end))
            .Returns(Task.CompletedTask);
        _cartService.Setup(service => service.EnsureHoldsAsync()).ReturnsAsync(false);
        _cartService.SetupGet(service => service.LastHoldError)
            .Returns("Wypożyczalnia jest zamknięta w wybranym terminie.");

        RenderComponent<MudPopoverProvider>();
        var component = RenderComponent<Products>();

        component.WaitForAssertion(() =>
        {
            var card = component.Find(".product-card");
            card.TextContent.Should().Contain("Rent Spot Kraków");
            card.TextContent.Should().Contain("Odbiór: Kraków");
        });
        component.Find("button.product-add-btn").Click();

        component.WaitForAssertion(() =>
        {
            var snackbarCalls = _snackbar.Invocations
                .Where(invocation => invocation.Method.Name == nameof(ISnackbar.Add))
                .ToList();
            snackbarCalls.Should().Contain(invocation =>
                invocation.Arguments.Count > 0 &&
                invocation.Arguments[0] != null &&
                invocation.Arguments[0]!.ToString()!.Contains(
                    "Wypożyczalnia jest zamknięta",
                    StringComparison.Ordinal));
            snackbarCalls.Should().NotContain(invocation =>
                invocation.Arguments.OfType<Severity>().Contains(Severity.Success));
        });
    }

    [Fact]
    public void Products_MobileMakesExplicitTenantVisibleAndKeepsTenantSwitcherAvailable()
    {
        var selectedTenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        Services.GetRequiredService<NavigationManager>()
            .NavigateTo($"products?tenantId={selectedTenantId:D}");

        var selectedProduct = new ProductDto
        {
            Id = Guid.NewGuid(),
            TenantId = selectedTenantId,
            TenantName = "Alpine Rent",
            Name = "Narty",
            Category = "Zimowe",
            DailyPrice = 100m,
            IsAvailable = true,
            AvailableQuantity = 2
        };
        var otherProduct = new ProductDto
        {
            Id = Guid.NewGuid(),
            TenantId = otherTenantId,
            TenantName = "Rowerownia",
            Name = "Rower",
            Category = "Rowery",
            DailyPrice = 80m,
            IsAvailable = true,
            AvailableQuantity = 1
        };
        _apiService
            .Setup(service => service.GetProductsPagedAsync(It.IsAny<ProductFilterRequest>()))
            .ReturnsAsync((ProductFilterRequest filter) => new ProductsPagedResponse
            {
                Items = [selectedProduct],
                TotalCount = 1,
                Page = filter.Page,
                PageSize = filter.PageSize,
                TotalPages = 1
            });
        _apiService
            .Setup(service => service.GetProductCatalogFacetsAsync())
            .ReturnsAsync(new ProductCatalogFacetsDto
            {
                Tenants =
                [
                    new ProductCatalogTenantFacetDto { TenantId = selectedTenantId, Name = selectedProduct.TenantName! },
                    new ProductCatalogTenantFacetDto { TenantId = otherTenantId, Name = otherProduct.TenantName! }
                ],
                TotalCount = 2,
                AvailableCount = 2,
                MinimumPrice = 80m,
                MaximumPrice = 100m,
                AveragePrice = 90m
            });

        RenderComponent<MudPopoverProvider>();
        var component = RenderComponent<Products>();
        component.InvokeAsync(() => component.Instance.OnScreenResize(true));

        component.WaitForAssertion(() =>
        {
            component.Find(".products-mobile-tenant-context").TextContent
                .Should().Contain("Alpine Rent");
            component.Find(".products-mobile-filter-btn").GetAttribute("aria-expanded")
                .Should().Be("false");
            var card = component.Find(".product-mobile-card");
            card.GetAttribute("role").Should().BeNull();
            card.GetAttribute("tabindex").Should().BeNull();
            card.QuerySelector("button.product-mobile-details-btn")
                .Should().NotBeNull();
            card.QuerySelector("button.product-mobile-cart-btn")
                .Should().NotBeNull();
            card.QuerySelector(".product-mobile-card-actions")
                .Should().NotBeNull();
            card.QuerySelector(".product-mobile-actions")
                .Should().BeNull("karta katalogu nie może odziedziczyć stałego paska akcji widoku szczegółów");
        });

        component.Find(".products-mobile-tenant-context button").Click();
        component.WaitForAssertion(() =>
        {
            component.Find("#mobile-product-filters").Should().NotBeNull();
            component.Find(".products-mobile-filter-btn").GetAttribute("aria-expanded")
                .Should().Be("true");
            component.Find("button.filter-chip-mobile").HasAttribute("aria-pressed").Should().BeTrue();
        });
        _apiService.Verify(service => service.GetProductCatalogFacetsAsync(), Times.Once);
    }

    [Fact]
    public void Products_LoadsMarketplaceFilterOptionsFromCompactFacetsEndpoint()
    {
        var firstTenantId = Guid.NewGuid();
        var secondTenantId = Guid.NewGuid();
        var firstProduct = new ProductDto
        {
            Id = Guid.NewGuid(),
            TenantId = firstTenantId,
            TenantName = "Tenant z pierwszej strony",
            Name = "Narty",
            DailyPrice = 100m,
            IsAvailable = true,
            AvailableQuantity = 1
        };
        var secondProduct = new ProductDto
        {
            Id = Guid.NewGuid(),
            TenantId = secondTenantId,
            TenantName = "Tenant z drugiej strony",
            Name = "Rower",
            DailyPrice = 80m,
            IsAvailable = true,
            AvailableQuantity = 1
        };

        _apiService
            .Setup(service => service.GetProductCatalogFacetsAsync())
            .ReturnsAsync(new ProductCatalogFacetsDto
            {
                Tenants =
                [
                    new ProductCatalogTenantFacetDto { TenantId = firstTenantId, Name = firstProduct.TenantName! },
                    new ProductCatalogTenantFacetDto { TenantId = secondTenantId, Name = secondProduct.TenantName! }
                ],
                TotalCount = 101,
                AvailableCount = 77,
                MinimumPrice = 80m,
                MaximumPrice = 100m,
                AveragePrice = 90m
            });
        _apiService
            .Setup(service => service.GetProductsPagedAsync(It.IsAny<ProductFilterRequest>()))
            .ReturnsAsync((ProductFilterRequest filter) => new ProductsPagedResponse
            {
                Items = [firstProduct],
                TotalCount = 101,
                AvailableCount = 77,
                AveragePrice = 90m,
                MinimumPrice = 80m,
                Page = filter.Page,
                PageSize = filter.PageSize,
                TotalPages = 9
            });

        RenderComponent<MudPopoverProvider>();
        var component = RenderComponent<Products>();

        component.WaitForAssertion(() =>
        {
            var stats = component.FindAll(".products-stat-value");
            stats.Should().HaveCountGreaterThanOrEqualTo(2);
            stats[0].TextContent.Trim().Should().Be("101");
            stats[1].TextContent.Trim().Should().Be("77");
        });
        _apiService.Verify(service => service.GetProductCatalogFacetsAsync(), Times.Once);
        _apiService.Verify(service => service.GetProductsPagedAsync(
            It.Is<ProductFilterRequest>(filter => filter.PageSize == 100)), Times.Never);
    }

    [Fact]
    public void Products_AlwaysShowsPickupContextAndUsesSeparateCardActions()
    {
        var withAddress = new ProductDto
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            TenantName = "Alpine Rent",
            Name = "Narty",
            PickupAddress = "ul. Górska 1",
            ImageUrl = "https://cdn.example.test/narty/w800.jpg",
            ImageVariantWidths = [400, 800, 1280],
            HasOriginalImage = true,
            DailyPrice = 100m,
            IsAvailable = true,
            AvailableQuantity = 2
        };
        var withoutLocation = new ProductDto
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            TenantName = "Rowerownia",
            Name = "Rower",
            DailyPrice = 80m,
            IsAvailable = true,
            AvailableQuantity = 1
        };
        _apiService
            .Setup(service => service.GetProductsPagedAsync(It.IsAny<ProductFilterRequest>()))
            .ReturnsAsync((ProductFilterRequest filter) => new ProductsPagedResponse
            {
                Items = [withAddress, withoutLocation],
                TotalCount = 2,
                Page = filter.Page,
                PageSize = filter.PageSize,
                TotalPages = 1
            });

        RenderComponent<MudPopoverProvider>();
        var component = RenderComponent<Products>();

        component.WaitForAssertion(() =>
        {
            var cards = component.FindAll(".product-card");
            cards.Should().HaveCount(2);
            cards[0].TextContent.Should().Contain("Odbiór: ul. Górska 1");
            cards[1].TextContent.Should().Contain("Odbiór: Adres odbioru do potwierdzenia");
            cards.Should().OnlyContain(card =>
                card.GetAttribute("role") == null &&
                card.GetAttribute("tabindex") == null &&
                card.QuerySelector("button.product-quick-btn-secondary") != null &&
                card.QuerySelector("button.product-add-btn") != null);

            var image = cards[0].QuerySelector("img")!;
            image.GetAttribute("src").Should().EndWith("/w400.jpg");
            image.GetAttribute("data-fallback-src").Should().Be(withAddress.ImageUrl);
        });

        component.InvokeAsync(() => component.Instance.OnScreenResize(true));
        component.WaitForAssertion(() =>
        {
            var cards = component.FindAll(".product-mobile-card");
            cards.Should().HaveCount(2);
            cards[0].TextContent.Should().Contain("Odbiór: ul. Górska 1");
            cards[1].TextContent.Should().Contain("Odbiór: Adres odbioru do potwierdzenia");
            cards.Should().OnlyContain(card =>
                card.GetAttribute("role") == null &&
                card.GetAttribute("tabindex") == null &&
                card.QuerySelector(".product-mobile-card-actions") != null &&
                card.QuerySelector(".product-mobile-actions") == null &&
                card.QuerySelector("button.product-mobile-details-btn") != null &&
                card.QuerySelector("button.product-mobile-cart-btn") != null);
        });
    }

    [Fact]
    public void ProductDetails_AddingSecondProductKeepsExistingTenantTerms()
    {
        var tenantId = Guid.NewGuid();
        var canonicalStart = DateTime.Today.AddDays(3).AddHours(10);
        var canonicalEnd = canonicalStart.AddHours(4);
        var existingProduct = new ProductDto
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            TenantName = "Alpine Rent",
            Name = "Narty",
            DailyPrice = 100m,
            HourlyPrice = 30m,
            IsAvailable = true,
            AvailableQuantity = 3
        };
        var addedProduct = new ProductDto
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            TenantName = "Alpine Rent",
            Name = "Kask",
            DailyPrice = 40m,
            HourlyPrice = 12m,
            IsAvailable = true,
            AvailableQuantity = 5
        };
        _cart.AddItem(existingProduct, 1, canonicalStart, canonicalEnd);
        var existingItem = _cart.Items.Single();
        existingItem.RentalType = RentalTypeDto.Hourly;
        existingItem.HoursRented = 4;
        existingItem.HoldId = Guid.NewGuid();

        _apiService.Setup(service => service.GetProductAsync(addedProduct.Id))
            .ReturnsAsync(addedProduct);
        _apiService.Setup(service => service.GetTenantLocationsAsync())
            .ReturnsAsync([]);
        _apiService.Setup(service => service.GetProductsAsync(1, 200))
            .ReturnsAsync([existingProduct, addedProduct]);
        _cartService
            .Setup(service => service.AddToCartAsync(
                addedProduct,
                1,
                canonicalStart,
                canonicalEnd))
            .Callback(() =>
            {
                _cart.AddItem(addedProduct, 1, canonicalStart, canonicalEnd);
                var addedItem = _cart.Items.Single(item => item.ProductId == addedProduct.Id);
                addedItem.RentalType = existingItem.RentalType;
                addedItem.HoursRented = existingItem.HoursRented;
                addedItem.HoldId = Guid.NewGuid();
            })
            .Returns(Task.CompletedTask);
        _cartService.Setup(service => service.EnsureHoldsAsync()).ReturnsAsync(true);

        RenderComponent<MudPopoverProvider>();
        var component = RenderComponent<ProductDetails>(parameters => parameters
            .Add(page => page.Id, addedProduct.Id));

        component.WaitForElement("button.product-details-add-btn").Click();

        _cartService.Verify(service => service.AddToCartAsync(
            addedProduct,
            1,
            canonicalStart,
            canonicalEnd), Times.Once);
        _cartService.Verify(service => service.UpdateRentalTypeAsync(
            It.IsAny<Guid>(),
            It.IsAny<RentalTypeDto>(),
            It.IsAny<int?>()), Times.Never);
        _cart.Items.Where(item => item.TenantId == tenantId)
            .Should().OnlyContain(item =>
                item.StartDate == canonicalStart &&
                item.EndDate == canonicalEnd &&
                item.RentalType == RentalTypeDto.Hourly &&
                item.HoursRented == 4);
    }

    [Fact]
    public void ProductDetails_ShowsPickupAddressAndResilientImagesOnBothLayouts()
    {
        var product = new ProductDto
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            TenantName = "Alpine Rent",
            Name = "Narty",
            PickupAddress = "ul. Górska 1",
            ImageUrl = "https://cdn.example.test/narty/w800.jpg",
            ImageVariantWidths = [400, 800, 1280],
            HasOriginalImage = true,
            DailyPrice = 100m,
            IsAvailable = true,
            AvailableQuantity = 2
        };
        var related = new ProductDto
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            TenantName = "Rowerownia Zakopane",
            Name = "Kijki",
            PickupAddress = "ul. Krupówki 9",
            ImageUrl = "https://cdn.example.test/kijki/w800.jpg",
            ImageVariantWidths = [400, 800],
            HasOriginalImage = true,
            DailyPrice = 20m,
            IsAvailable = true,
            AvailableQuantity = 3
        };
        _apiService.Setup(service => service.GetProductAsync(product.Id)).ReturnsAsync(product);
        _apiService.Setup(service => service.GetTenantLocationsAsync()).ReturnsAsync([]);
        _apiService.Setup(service => service.GetProductsAsync(1, 200)).ReturnsAsync([product, related]);

        RenderComponent<MudPopoverProvider>();
        var component = RenderComponent<ProductDetails>(parameters => parameters
            .Add(page => page.Id, product.Id));

        component.WaitForAssertion(() =>
        {
            component.Find(".product-details-info").TextContent
                .Should().Contain("Odbiór: ul. Górska 1");
            var mainImage = component.Find("img.product-details-image");
            mainImage.GetAttribute("src").Should().EndWith("/w1280.jpg");
            mainImage.GetAttribute("data-fallback-src").Should().Be(product.ImageUrl);
            var relatedImage = component.Find("img.related-product-image");
            relatedImage.GetAttribute("src").Should().EndWith("/w400.jpg");
            relatedImage.GetAttribute("data-fallback-src").Should().Be(related.ImageUrl);
            component.Find(".related-product-info").TextContent.Should().Contain("Inna wypożyczalnia");
            component.Find(".related-product-info").TextContent.Should().Contain("Rowerownia Zakopane");
            component.Find(".related-product-info").TextContent.Should().Contain("ul. Krupówki 9");
        });

        component.InvokeAsync(() => component.Instance.OnScreenResize(true));
        component.WaitForAssertion(() =>
        {
            component.Find(".product-mobile-description").TextContent
                .Should().Contain("Odbiór: ul. Górska 1");
            component.Find(".product-mobile-image img")
                .GetAttribute("data-fallback-src").Should().Be(product.ImageUrl);
        });
    }

    [Fact]
    public void SelectTenant_PersistsAndNavigatesWithStableTenantId()
    {
        var tenant = new TenantInfo { Id = Guid.NewGuid(), Name = "Ta sama nazwa" };
        _tenantListHandler.Tenants = [tenant];

        var component = RenderComponent<SelectTenant>();
        component.WaitForElement("div.cursor-pointer").Click();

        _localStorage.Verify(storage => storage.SetItemAsync(
            "selected_tenant_id", tenant.Id.ToString(), default), Times.Once);
        Services.GetRequiredService<NavigationManager>().Uri
            .Should().EndWith($"/products?tenantId={tenant.Id:D}");
    }

    [Fact]
    public void Home_CategoryCardsUseExactCategoryFiltersAndContainsNoDeadRoutes()
    {
        var component = RenderComponent<Home>();
        var links = component.FindAll("a")
            .Select(element => element.GetAttribute("href"))
            .Where(href => href is not null)
            .ToList();
        var categoryLinks = component.FindAll("a.sr-category-card")
            .Select(element => element.GetAttribute("href"))
            .ToList();

        categoryLinks.Should().Equal(
            "products?category=Narty",
            "products?category=Snowboard",
            "products?category=Rower",
            "products?category=SUP");
        links.Should().Contain("my-rentals");
        links.Should().Contain("terms");
        links.Should().Contain("privacy");

        links.Should().NotContain(new[] { "rentals", "about", "faq" });
        component.Markup.Should().NotContain("500+");
        component.Markup.Should().NotContain("10k+");
        component.Markup.Should().NotContain("4.9");
        component.Markup.Should().NotContain("24/7");
        component.Markup.Should().NotContain("od 49 zł");
        component.Markup.Should().NotContain("Pełne ubezpieczenie");
        component.Markup.Should().NotContain("bez dodatkowych opłat");
        component.Markup.Should().Contain("Zobacz aktualną ofertę");
    }

    private sealed class TenantListHandler : HttpMessageHandler
    {
        public List<TenantInfo> Tenants { get; set; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = JsonContent.Create(Tenants)
            });
    }
}
