using SportRental.Infrastructure.Domain;
using FluentAssertions;

namespace SportRental.Admin.Tests.Data.Domain;

public class ProductTests
{
    [Fact]
    public void Product_DefaultConstructor_ShouldInitializeWithDefaults()
    {
        // Act
        var product = new Product();

        // Assert
        product.Id.Should().Be(Guid.Empty); // Default Guid value
        product.TenantId.Should().Be(Guid.Empty);
        product.Name.Should().Be(string.Empty); // Has default value
        product.Sku.Should().Be(string.Empty); // Has default value
        product.DailyPrice.Should().Be(0);
        product.AvailableQuantity.Should().Be(1); // Default is 1
        product.IsActive.Should().BeTrue(); // Default is true
        product.Category.Should().BeNull();
        product.ImageUrl.Should().BeNull();
        product.CreatedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Theory]
    [InlineData("Rower górski Trek")]
    [InlineData("Kask rowerowy Specialized")]
    [InlineData("Namiot 4-osobowy Coleman")]
    public void Product_SetName_ShouldAcceptValidNames(string name)
    {
        // Arrange
        var product = new Product();

        // Act
        product.Name = name;

        // Assert
        product.Name.Should().Be(name);
    }

    [Theory]
    [InlineData("BIKE001")]
    [InlineData("HELMET-RED-L")]
    [InlineData("TENT_4P_BLUE")]
    public void Product_SetSku_ShouldAcceptValidSkus(string sku)
    {
        // Arrange
        var product = new Product();

        // Act
        product.Sku = sku;

        // Assert
        product.Sku.Should().Be(sku);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(25.50)]
    [InlineData(100.00)]
    [InlineData(999.99)]
    public void Product_SetDailyPrice_ShouldAcceptValidPrices(decimal price)
    {
        // Arrange
        var product = new Product();

        // Act
        product.DailyPrice = price;

        // Assert
        product.DailyPrice.Should().Be(price);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(100)]
    public void Product_SetAvailableQuantity_ShouldAcceptValidQuantities(int quantity)
    {
        // Arrange
        var product = new Product();

        // Act
        product.AvailableQuantity = quantity;

        // Assert
        product.AvailableQuantity.Should().Be(quantity);
    }

    [Theory]
    [InlineData("Rowery")]
    [InlineData("Akcesoria")]
    [InlineData("Sprzęt turystyczny")]
    [InlineData("Obuwie")]
    public void Product_SetCategory_ShouldAcceptValidCategories(string category)
    {
        // Arrange
        var product = new Product();

        // Act
        product.Category = category;

        // Assert
        product.Category.Should().Be(category);
    }

    [Fact]
    public void Product_SetImageAlt_ShouldAcceptText()
    {
        // Arrange
        var product = new Product();
        var imageAlt = "Profesjonalny rower górski Trek - widok z boku";

        // Act
        product.ImageAlt = imageAlt;

        // Assert
        product.ImageAlt.Should().Be(imageAlt);
    }

    [Theory]
    [InlineData("https://example.com/images/product1.jpg")]
    [InlineData("/uploads/bike_red.png")]
    [InlineData("product.jpg")]
    public void Product_SetImageUrl_ShouldAcceptValidUrls(string imageUrl)
    {
        // Arrange
        var product = new Product();

        // Act
        product.ImageUrl = imageUrl;

        // Assert
        product.ImageUrl.Should().Be(imageUrl);
    }

    [Fact]
    public void Product_WithAllProperties_ShouldMaintainValues()
    {
        // Arrange
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var name = "Rower górski Trek X-Caliber";
        var sku = "BIKE-TREK-XC-L";
        var dailyPrice = 75.50m;
        var availableQuantity = 3;
        var category = "Rowery";
        var imageUrl = "https://example.com/images/trek-xcaliber.jpg";
        var imageAlt = "Rower Trek X-Caliber - widok z boku";

        // Act
        var product = new Product
        {
            Id = id,
            TenantId = tenantId,
            Name = name,
            Sku = sku,
            DailyPrice = dailyPrice,
            AvailableQuantity = availableQuantity,
            Category = category,
            ImageUrl = imageUrl,
            ImageAlt = imageAlt
        };

        // Assert
        product.Id.Should().Be(id);
        product.TenantId.Should().Be(tenantId);
        product.Name.Should().Be(name);
        product.Sku.Should().Be(sku);
        product.DailyPrice.Should().Be(dailyPrice);
        product.AvailableQuantity.Should().Be(availableQuantity);
        product.Category.Should().Be(category);
        product.ImageUrl.Should().Be(imageUrl);
        product.ImageAlt.Should().Be(imageAlt);
    }

    [Fact]
    public void Product_IsAvailable_ShouldReturnCorrectStatus()
    {
        // Arrange & Act & Assert
        var availableProduct = new Product { AvailableQuantity = 5 };
        availableProduct.AvailableQuantity.Should().BeGreaterThan(0);

        var unavailableProduct = new Product { AvailableQuantity = 0 };
        unavailableProduct.AvailableQuantity.Should().Be(0);
    }

    [Theory]
    [InlineData(50.00, 7, 350.00)]
    [InlineData(25.50, 3, 76.50)]
    [InlineData(100.00, 1, 100.00)]
    public void Product_CalculateRentalCost_ShouldReturnCorrectAmount(decimal dailyPrice, int days, decimal expectedTotal)
    {
        // Arrange
        var product = new Product { DailyPrice = dailyPrice };

        // Act
        var totalCost = product.DailyPrice * days;

        // Assert
        totalCost.Should().Be(expectedTotal);
    }
}