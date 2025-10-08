using SportRental.Infrastructure.Domain;
using FluentAssertions;

namespace SportRental.Admin.Tests.Data.Domain;

public class RentalItemTests
{
    [Fact]
    public void RentalItem_DefaultConstructor_ShouldInitializeWithDefaults()
    {
        // Act
        var rentalItem = new RentalItem();

        // Assert
        rentalItem.Id.Should().Be(Guid.Empty);
        rentalItem.RentalId.Should().Be(Guid.Empty);
        rentalItem.ProductId.Should().Be(Guid.Empty);
        rentalItem.Quantity.Should().Be(1); // Default value
        rentalItem.PricePerDay.Should().Be(0);
        rentalItem.Subtotal.Should().Be(0);
    }

    [Theory]
    [InlineData(1, 50.00, 50.00)]
    [InlineData(2, 25.50, 51.00)]
    [InlineData(5, 10.00, 50.00)]
    public void RentalItem_SetQuantityAndPrice_ShouldCalculateCorrectSubtotal(int quantity, decimal pricePerDay, decimal expectedSubtotal)
    {
        // Arrange
        var rentalItem = new RentalItem
        {
            Quantity = quantity,
            PricePerDay = pricePerDay
        };

        // Act
        rentalItem.Subtotal = pricePerDay * quantity;

        // Assert
        rentalItem.Subtotal.Should().Be(expectedSubtotal);
    }

    [Fact]
    public void RentalItem_WithAllProperties_ShouldMaintainValues()
    {
        // Arrange
        var id = Guid.NewGuid();
        var rentalId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var quantity = 3;
        var pricePerDay = 25.50m;
        var subtotal = 76.50m;

        // Act
        var rentalItem = new RentalItem
        {
            Id = id,
            RentalId = rentalId,
            ProductId = productId,
            Quantity = quantity,
            PricePerDay = pricePerDay,
            Subtotal = subtotal
        };

        // Assert
        rentalItem.Id.Should().Be(id);
        rentalItem.RentalId.Should().Be(rentalId);
        rentalItem.ProductId.Should().Be(productId);
        rentalItem.Quantity.Should().Be(quantity);
        rentalItem.PricePerDay.Should().Be(pricePerDay);
        rentalItem.Subtotal.Should().Be(subtotal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-10)]
    public void RentalItem_WithZeroOrNegativeQuantity_ShouldAllowValue(int quantity)
    {
        // Arrange & Act
        var rentalItem = new RentalItem { Quantity = quantity };

        // Assert
        rentalItem.Quantity.Should().Be(quantity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5.50)]
    public void RentalItem_WithZeroOrNegativePrice_ShouldAllowValue(decimal price)
    {
        // Arrange & Act
        var rentalItem = new RentalItem { PricePerDay = price };

        // Assert
        rentalItem.PricePerDay.Should().Be(price);
    }
}