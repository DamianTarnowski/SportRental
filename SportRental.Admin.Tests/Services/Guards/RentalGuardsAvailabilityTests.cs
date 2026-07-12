using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SportRental.Admin.Services.Guards;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;

namespace SportRental.Admin.Tests.Services.Guards;

public sealed class RentalGuardsAvailabilityTests
{
    private static readonly DateTime RequestedStartUtc = new(2026, 7, 11, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime RequestedEndUtc = new(2026, 7, 13, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ReservationConflict_AllowsQuantityWithinRemainingStock()
    {
        await using var fixture = await AvailabilityFixture.CreateAsync(availableQuantity: 20);
        await fixture.AddRentalAsync(quantity: 2, RentalStatus.Pending);
        await fixture.AddRentalAsync(quantity: 3, RentalStatus.Confirmed);

        var conflict = await RentalGuards.GetReservationConflictAsync(
            fixture.Db,
            fixture.TenantId,
            fixture.ProductId,
            requestedQuantity: 15,
            RequestedStartUtc,
            RequestedEndUtc);

        conflict.Should().BeNull();
    }

    [Fact]
    public async Task ReservationConflict_ReportsAvailableAndRequestedQuantitiesWhenStockIsExceeded()
    {
        await using var fixture = await AvailabilityFixture.CreateAsync(availableQuantity: 20);
        await fixture.AddRentalAsync(quantity: 2, RentalStatus.Confirmed);
        await fixture.AddRentalAsync(quantity: 3, RentalStatus.Active);

        var conflict = await RentalGuards.GetReservationConflictAsync(
            fixture.Db,
            fixture.TenantId,
            fixture.ProductId,
            requestedQuantity: 16,
            RequestedStartUtc,
            RequestedEndUtc);

        conflict.Should().NotBeNull();
        conflict.Should().Contain("Dostępne: 15");
        conflict.Should().Contain("wybrano: 16");
    }

    [Fact]
    public async Task ReservationConflict_ExcludesEditedRentalFromReservedQuantity()
    {
        await using var fixture = await AvailabilityFixture.CreateAsync(availableQuantity: 20);
        var editedRentalId = await fixture.AddRentalAsync(quantity: 20, RentalStatus.Confirmed);

        var conflict = await RentalGuards.GetReservationConflictAsync(
            fixture.Db,
            fixture.TenantId,
            fixture.ProductId,
            requestedQuantity: 20,
            RequestedStartUtc,
            RequestedEndUtc,
            excludeRentalId: editedRentalId);

        conflict.Should().BeNull();
    }

    [Fact]
    public async Task ReservationConflict_DoesNotCountCompletedOrPhysicallyReturnedRentals()
    {
        await using var fixture = await AvailabilityFixture.CreateAsync(availableQuantity: 20);
        await fixture.AddRentalAsync(quantity: 10, RentalStatus.Completed);
        await fixture.AddRentalAsync(
            quantity: 10,
            RentalStatus.Active,
            returnedAtUtc: RequestedStartUtc.AddHours(-1));

        var conflict = await RentalGuards.GetReservationConflictAsync(
            fixture.Db,
            fixture.TenantId,
            fixture.ProductId,
            requestedQuantity: 20,
            RequestedStartUtc,
            RequestedEndUtc);

        conflict.Should().BeNull();
    }

    private sealed class AvailabilityFixture : IAsyncDisposable
    {
        private readonly Guid _customerId = Guid.NewGuid();

        private AvailabilityFixture(ApplicationDbContext db, Guid tenantId, Guid productId)
        {
            Db = db;
            TenantId = tenantId;
            ProductId = productId;
        }

        public ApplicationDbContext Db { get; }
        public Guid TenantId { get; }
        public Guid ProductId { get; }

        public static async Task<AvailabilityFixture> CreateAsync(int availableQuantity)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"rental-guards-availability-{Guid.NewGuid():N}")
                .Options;
            var db = new ApplicationDbContext(options);
            var tenantId = Guid.NewGuid();
            var productId = Guid.NewGuid();
            var fixture = new AvailabilityFixture(db, tenantId, productId);

            db.Tenants.Add(new Tenant { Id = tenantId, Name = "Testowa wypożyczalnia" });
            db.Customers.Add(new Customer
            {
                Id = fixture._customerId,
                TenantId = tenantId,
                FullName = "Jan Testowy"
            });
            db.Products.Add(new Product
            {
                Id = productId,
                TenantId = tenantId,
                Name = "Rower testowy",
                Sku = "ROWER-1",
                AvailableQuantity = availableQuantity
            });
            await db.SaveChangesAsync();

            return fixture;
        }

        public async Task<Guid> AddRentalAsync(
            int quantity,
            RentalStatus status,
            DateTime? returnedAtUtc = null)
        {
            var rentalId = Guid.NewGuid();
            Db.Rentals.Add(new Rental
            {
                Id = rentalId,
                TenantId = TenantId,
                CustomerId = _customerId,
                StartDateUtc = RequestedStartUtc.AddDays(-1),
                EndDateUtc = RequestedEndUtc.AddDays(-1),
                Status = status,
                ReturnedAtUtc = returnedAtUtc
            });
            Db.RentalItems.Add(new RentalItem
            {
                Id = Guid.NewGuid(),
                RentalId = rentalId,
                ProductId = ProductId,
                Quantity = quantity
            });
            await Db.SaveChangesAsync();
            return rentalId;
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
