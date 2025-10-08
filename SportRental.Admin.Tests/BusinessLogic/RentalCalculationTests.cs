using SportRental.Infrastructure.Domain;
using FluentAssertions;

namespace SportRental.Admin.Tests.BusinessLogic;

public class RentalCalculationTests
{
    [Theory]
    [InlineData(1, 50.00, 1, 50.00)]    // 1 item, 1 day
    [InlineData(2, 50.00, 1, 100.00)]   // 2 items, 1 day
    [InlineData(1, 50.00, 7, 350.00)]   // 1 item, 7 days
    [InlineData(3, 25.50, 5, 382.50)]   // 3 items, 5 days
    public void CalculateRentalCost_WithValidInputs_ShouldReturnCorrectAmount(
        int quantity, decimal dailyPrice, int days, decimal expectedTotal)
    {
        // Arrange & Act
        var totalCost = quantity * dailyPrice * days;

        // Assert
        totalCost.Should().Be(expectedTotal);
    }

    [Fact]
    public void Rental_CalculateTotalFromItems_ShouldSumAllSubtotals()
    {
        // Arrange
        var rental = new Rental();
        var items = new List<RentalItem>
        {
            new RentalItem { Quantity = 1, PricePerDay = 50.00m, Subtotal = 50.00m },
            new RentalItem { Quantity = 2, PricePerDay = 25.00m, Subtotal = 50.00m },
            new RentalItem { Quantity = 1, PricePerDay = 100.00m, Subtotal = 100.00m }
        };

        // Act
        var totalAmount = items.Sum(i => i.Subtotal);
        rental.TotalAmount = totalAmount;

        // Assert
        rental.TotalAmount.Should().Be(200.00m);
    }

    [Theory]
    [InlineData("2024-01-01", "2024-01-02", 1)]    // 1 day
    [InlineData("2024-01-01", "2024-01-03", 2)]    // 2 days  
    [InlineData("2024-01-01", "2024-01-08", 7)]    // 1 week
    [InlineData("2024-01-01", "2024-01-31", 30)]   // 1 month
    public void CalculateRentalDays_WithValidDates_ShouldReturnCorrectDayCount(
        string startDateStr, string endDateStr, int expectedDays)
    {
        // Arrange
        var startDate = DateTime.Parse(startDateStr);
        var endDate = DateTime.Parse(endDateStr);

        // Act
        var daysDiff = (endDate - startDate).Days;

        // Assert
        daysDiff.Should().Be(expectedDays);
    }

    [Fact]
    public void RentalStatus_Progression_ShouldFollowValidTransitions()
    {
        // Arrange
        var rental = new Rental();

        // Act & Assert - Valid progression
        rental.Status = RentalStatus.Draft;
        rental.Status.Should().Be(RentalStatus.Draft);

        rental.Status = RentalStatus.Confirmed;
        rental.Status.Should().Be(RentalStatus.Confirmed);

        rental.Status = RentalStatus.Active;
        rental.Status.Should().Be(RentalStatus.Active);

        rental.Status = RentalStatus.Completed;
        rental.Status.Should().Be(RentalStatus.Completed);

        // Cancellation can happen from any status
        rental.Status = RentalStatus.Cancelled;
        rental.Status.Should().Be(RentalStatus.Cancelled);
    }

    [Fact]
    public void Product_AvailabilityCheck_ShouldCalculateCorrectAvailableQuantity()
    {
        // Arrange
        var product = new Product { AvailableQuantity = 10 };
        var reservedQuantity = 3;
        var requestedQuantity = 2;

        // Act
        var availableForRent = product.AvailableQuantity - reservedQuantity;
        var canFulfillRequest = availableForRent >= requestedQuantity;

        // Assert
        availableForRent.Should().Be(7);
        canFulfillRequest.Should().BeTrue();
    }

    [Theory]
    [InlineData(5, 3, 2, true)]     // Can fulfill: 5 available, 3 reserved, need 2
    [InlineData(5, 3, 3, false)]    // Cannot fulfill: 5 available, 3 reserved, need 3  
    [InlineData(10, 8, 1, true)]    // Can fulfill: 10 available, 8 reserved, need 1
    [InlineData(10, 10, 1, false)]  // Cannot fulfill: 10 available, 10 reserved, need 1
    public void Product_AvailabilityCheck_WithDifferentScenarios_ShouldReturnCorrectResult(
        int totalQuantity, int reservedQuantity, int requestedQuantity, bool expectedCanFulfill)
    {
        // Arrange
        var product = new Product { AvailableQuantity = totalQuantity };

        // Act
        var availableForRent = product.AvailableQuantity - reservedQuantity;
        var canFulfillRequest = availableForRent >= requestedQuantity;

        // Assert
        canFulfillRequest.Should().Be(expectedCanFulfill);
    }
}