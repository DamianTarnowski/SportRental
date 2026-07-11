using FluentAssertions;
using SportRental.Shared.Models;

namespace SportRental.Client.Tests.Models;

public class ProductDtoTests
{
    [Fact]
    public void FilterQuery_PricesUseInvariantDecimalSeparator()
    {
        var previousCulture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture =
                System.Globalization.CultureInfo.GetCultureInfo("pl-PL");

            var query = new ProductFilterRequest
            {
                MinPrice = 10.5m,
                MaxPrice = 99.95m
            }.ToQueryString();

            query.Should().Contain("minPrice=10.5");
            query.Should().Contain("maxPrice=99.95");
            query.Should().NotContain("10,5");
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public void GetImageUrl_WithoutStoredImage_UsesBuiltInEmptyState()
    {
        var product = new ProductDto { Category = "Snowboard" };

        product.GetImageUrl(400).Should().BeEmpty();
    }

    [Theory]
    [InlineData(300, "w400")]
    [InlineData(400, "w400")]
    [InlineData(600, "w800")]
    [InlineData(800, "w800")]
    [InlineData(1200, "w1280")]
    [InlineData(1600, "w1280")]
    public void GetImageUrl_MapsRequestsToGeneratedVariantWidths(int requestedWidth, string expectedStem)
    {
        var product = new ProductDto
        {
            ImageUrl = "https://cdn.example.test/products/item/w800.webp?sig=abc#photo",
            ImageVariantWidths = [400, 800, 1280]
        };

        product.GetImageUrl(requestedWidth).Should().Be(
            $"https://cdn.example.test/products/item/{expectedStem}.webp?sig=abc#photo");
    }

    [Fact]
    public void ImageHelpers_DoNotRewriteArbitraryStoredFileNames()
    {
        var imageUrl = "https://cdn.example.test/products/item/customer-photo.jpg?sig=abc";
        var product = new ProductDto { ImageUrl = imageUrl };

        product.GetImageUrl(400).Should().Be(imageUrl);
        product.GetOriginalImageUrl().Should().Be(imageUrl);
        product.GetImageSrcSet().Should().BeEmpty();
    }

    [Fact]
    public void ImageHelpers_ProduceOnlySupportedVariantsForGeneratedImage()
    {
        var product = new ProductDto
        {
            ImageUrl = "https://cdn.example.test/products/item/w800.jpg",
            ImageVariantWidths = [400, 800, 1280],
            HasOriginalImage = true
        };

        product.GetOriginalImageUrl().Should().Be(
            "https://cdn.example.test/products/item/original.jpg");
        product.GetImageSrcSet().Should().Be(
            "https://cdn.example.test/products/item/w400.jpg 400w, " +
            "https://cdn.example.test/products/item/w800.jpg 800w, " +
            "https://cdn.example.test/products/item/w1280.jpg 1280w");
    }

    [Fact]
    public void LegacyGeneratedName_WithoutManifest_UsesOnlyStoredUrl()
    {
        const string storedUrl = "https://cdn.example.test/products/item/w800.jpg";
        var product = new ProductDto { ImageUrl = storedUrl };

        product.GetImageUrl(400).Should().Be(storedUrl);
        product.GetImageUrl(1280).Should().Be(storedUrl);
        product.GetOriginalImageUrl().Should().Be(storedUrl);
        product.GetImageSrcSet().Should().BeEmpty();
    }

    [Fact]
    public void ImageHelpers_SelectOnlyVariantsDeclaredByManifest()
    {
        var product = new ProductDto
        {
            ImageUrl = "https://cdn.example.test/products/item/w800.jpg",
            ImageVariantWidths = [800, 400, 800],
            HasOriginalImage = false
        };

        product.GetImageUrl(300).Should().EndWith("/w400.jpg");
        product.GetImageUrl(600).Should().EndWith("/w800.jpg");
        product.GetImageUrl(1280).Should().EndWith("/w800.jpg");
        product.GetOriginalImageUrl().Should().EndWith("/w800.jpg");
        product.GetImageSrcSet().Should().Be(
            "https://cdn.example.test/products/item/w400.jpg 400w, " +
            "https://cdn.example.test/products/item/w800.jpg 800w");
    }

    [Fact]
    public void InlineRasterDataUri_IsNeverRewrittenEvenWithStaleManifest()
    {
        const string inlineImage = "data:image/jpeg;base64,/9j/4AAQSkZJRg==";
        var product = new ProductDto
        {
            ImageUrl = inlineImage,
            ImageVariantWidths = [400, 800, 1280],
            HasOriginalImage = true
        };

        product.GetImageUrl(400).Should().Be(inlineImage);
        product.GetOriginalImageUrl().Should().Be(inlineImage);
        product.GetImageSrcSet().Should().BeEmpty();
    }

    [Theory]
    [InlineData(" Kraków ", "ul. Górska 1", "ul. Górska 1")]
    [InlineData(null, " ul. Górska 1 ", "ul. Górska 1")]
    [InlineData(" ", " ", "Adres odbioru do potwierdzenia")]
    public void PickupDisplay_PrefersFullAddressThenCityAndAlwaysReturnsContext(
        string? city,
        string? pickupAddress,
        string expected)
    {
        var product = new ProductDto
        {
            City = city,
            PickupAddress = pickupAddress
        };

        product.GetPickupDisplayText().Should().Be(expected);
    }
}
