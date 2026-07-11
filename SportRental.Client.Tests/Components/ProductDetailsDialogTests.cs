using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using SportRental.Client.Components;
using SportRental.Shared.Models;

namespace SportRental.Client.Tests.Components;

public sealed class ProductDetailsDialogTests : TestContext
{
    [Fact]
    public async Task AddButton_DelegatesProductToCatalogCallback()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var product = new ProductDto
        {
            Id = Guid.NewGuid(),
            Name = "Narty testowe",
            DailyPrice = 100m,
            IsAvailable = true,
            AvailableQuantity = 1
        };
        ProductDto? selectedProduct = null;

        RenderComponent<MudPopoverProvider>();
        var dialogProvider = RenderComponent<MudDialogProvider>();
        var parameters = new DialogParameters
        {
            [nameof(ProductDetailsDialog.Product)] = product,
            [nameof(ProductDetailsDialog.AddToCart)] =
                EventCallback.Factory.Create<ProductDto>(this, value => selectedProduct = value)
        };

        await Services.GetRequiredService<IDialogService>()
            .ShowAsync<ProductDetailsDialog>("Szczegóły produktu", parameters);

        dialogProvider.WaitForElement("button");
        dialogProvider.FindAll("button")
            .Single(button => button.TextContent.Contains("Do koszyka", StringComparison.Ordinal))
            .Click();

        selectedProduct.Should().BeSameAs(product);
    }

    [Fact]
    public async Task Dialog_UsesPickupAddressWhenCityIsMissing()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var product = new ProductDto
        {
            Name = "Narty testowe",
            TenantName = "Alpine Rent",
            PickupAddress = " ul. Górska 1 ",
            DailyPrice = 100m,
            IsAvailable = true,
            AvailableQuantity = 1
        };

        RenderComponent<MudPopoverProvider>();
        var dialogProvider = RenderComponent<MudDialogProvider>();
        await Services.GetRequiredService<IDialogService>().ShowAsync<ProductDetailsDialog>(
            "Szczegóły produktu",
            new DialogParameters { [nameof(ProductDetailsDialog.Product)] = product });

        dialogProvider.WaitForAssertion(() =>
        {
            dialogProvider.Find(".product-dialog-tenant").TextContent
                .Should().Contain("Odbiór: ul. Górska 1");
        });
    }

    [Fact]
    public async Task Dialog_ShowsPickupFallbackAndKnownImageFallbackSource()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var product = new ProductDto
        {
            Name = "Narty testowe",
            ImageUrl = "https://cdn.example.test/item/w800.jpg",
            DailyPrice = 100m,
            IsAvailable = true,
            AvailableQuantity = 1
        };

        RenderComponent<MudPopoverProvider>();
        var dialogProvider = RenderComponent<MudDialogProvider>();
        await Services.GetRequiredService<IDialogService>().ShowAsync<ProductDetailsDialog>(
            "Szczegóły produktu",
            new DialogParameters { [nameof(ProductDetailsDialog.Product)] = product });

        dialogProvider.WaitForAssertion(() =>
        {
            dialogProvider.Find(".product-dialog-tenant").TextContent
                .Should().Contain("Odbiór: Adres odbioru do potwierdzenia");
            var image = dialogProvider.Find("img.product-dialog-image");
            image.GetAttribute("src").Should().Be(product.ImageUrl);
            image.GetAttribute("data-fallback-src").Should().Be(product.ImageUrl);
        });
    }
}
