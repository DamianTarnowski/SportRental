using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;
using Microsoft.EntityFrameworkCore;

namespace SportRental.Admin.Tests;

public sealed class RentalLogicTests
{
    private static ApplicationDbContext CreateInMemory()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task AvailabilityValidation_DetectsOverlaps()
    {
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        await using var db = CreateInMemory();
        await db.Products.AddAsync(new Product
        {
            Id = productId,
            TenantId = tenantId,
            Name = "Kijki",
            Sku = "K-1",
            DailyPrice = 10,
            AvailableQuantity = 3,
            CreatedAtUtc = DateTime.UtcNow
        });
        var customerId = Guid.NewGuid();
        await db.Customers.AddAsync(new Customer { Id = customerId, TenantId = tenantId, FullName = "Jan" });

        var existingRental = new Rental
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CustomerId = customerId,
            StartDateUtc = DateTime.UtcNow.Date,
            EndDateUtc = DateTime.UtcNow.Date.AddDays(2),
            Status = RentalStatus.Confirmed,
            CreatedAtUtc = DateTime.UtcNow
        };
        await db.Rentals.AddAsync(existingRental);
        await db.RentalItems.AddAsync(new RentalItem
        {
            Id = Guid.NewGuid(),
            RentalId = existingRental.Id,
            ProductId = productId,
            Quantity = 2,
            PricePerDay = 10,
            Subtotal = 20
        });
        await db.SaveChangesAsync();

        var reqStart = DateTime.UtcNow.Date.AddDays(1);
        var reqEnd = reqStart.AddDays(2);
        var overlappingReservedQty = await db.RentalItems
            .Where(ri => ri.ProductId == productId)
            .Join(db.Rentals, ri => ri.RentalId, r => r.Id, (ri, r) => new { ri, r })
            .Where(x => x.r.TenantId == tenantId
                        && x.r.Status != RentalStatus.Cancelled
                        && x.r.EndDateUtc > reqStart
                        && x.r.StartDateUtc < reqEnd)
            .SumAsync(x => (int?)x.ri.Quantity) ?? 0;

        // Dostępne 3, zarezerwowane 2, żądane 2 => konflikt
        var requestedQty = 2;
        Assert.True(overlappingReservedQty + requestedQty > 3);
    }

    [Fact]
    public void DaysCalculation_RoundsUpAndMin1()
    {
        static int Days(DateTime start, DateTime end)
            => Math.Max(1, (int)Math.Ceiling((end - start).TotalDays));

        var s1 = new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var e1 = new DateTime(2024, 1, 1, 18, 0, 0, DateTimeKind.Utc);
        Assert.Equal(1, Days(s1, e1));

        var s2 = new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var e2 = new DateTime(2024, 1, 2, 11, 0, 0, DateTimeKind.Utc);
        Assert.Equal(2, Days(s2, e2));

        var s3 = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var e3 = new DateTime(2024, 1, 4, 0, 0, 1, DateTimeKind.Utc);
        Assert.Equal(4, Days(s3, e3));
    }
}


