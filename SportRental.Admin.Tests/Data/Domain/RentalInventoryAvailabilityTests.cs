using FluentAssertions;
using SportRental.Infrastructure.Domain;

namespace SportRental.Admin.Tests.Data.Domain;

public sealed class RentalInventoryAvailabilityTests
{
    [Theory]
    [InlineData(RentalStatus.Draft, true)]
    [InlineData(RentalStatus.Pending, true)]
    [InlineData(RentalStatus.Confirmed, true)]
    [InlineData(RentalStatus.Active, true)]
    [InlineData(RentalStatus.Completed, false)]
    [InlineData(RentalStatus.Cancelled, false)]
    public void BlocksInventory_ReflectsCompleteRentalLifecycle(
        RentalStatus status,
        bool expected)
    {
        RentalInventoryAvailability.BlocksInventory(status).Should().Be(expected);
    }

    [Theory]
    [InlineData(RentalStatus.Draft)]
    [InlineData(RentalStatus.Pending)]
    [InlineData(RentalStatus.Confirmed)]
    [InlineData(RentalStatus.Active)]
    [InlineData(RentalStatus.Completed)]
    [InlineData(RentalStatus.Cancelled)]
    public void BlocksInventory_AfterPhysicalReturn_IsAlwaysFalse(RentalStatus status)
    {
        var rental = new Rental
        {
            Status = status,
            ReturnedAtUtc = DateTime.UtcNow
        };

        RentalInventoryAvailability.BlocksInventory(rental).Should().BeFalse();
    }

    [Fact]
    public void WhereInventoryBlocking_FiltersAvailabilityWithoutRemovingHistory()
    {
        var rentals = Enum.GetValues<RentalStatus>()
            .Select(status => new Rental { Id = Guid.NewGuid(), Status = status })
            .ToList();

        var blocking = rentals
            .AsQueryable()
            .WhereInventoryBlocking()
            .Select(rental => rental.Status)
            .ToList();

        blocking.Should().BeEquivalentTo(new[]
        {
            RentalStatus.Draft,
            RentalStatus.Pending,
            RentalStatus.Confirmed,
            RentalStatus.Active
        });
        rentals.Should().HaveCount(Enum.GetValues<RentalStatus>().Length);
        rentals.Should().Contain(rental => rental.Status == RentalStatus.Completed);
        rentals.Should().Contain(rental => rental.Status == RentalStatus.Cancelled);
    }
}
