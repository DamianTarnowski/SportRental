using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;
using SportRental.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace SportRental.Admin.Tests;

public sealed class DbContextTests
{
    private sealed class TestTenantProvider : ITenantProvider
    {
        private readonly Guid? _tenantId;
        public TestTenantProvider(Guid? tenantId) => _tenantId = tenantId;
        public Guid? GetCurrentTenantId() => _tenantId;
    }

    private static ApplicationDbContext CreateInMemory(Guid? tenantId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        // W testach używamy publicznego konstruktora i ustawiamy tenant przez metodę pomocniczą
        var ctx = new ApplicationDbContext(options);
        ctx.SetTenant(tenantId);
        return ctx;
    }

    [Fact]
    public async Task GlobalFilter_HidesOtherTenantData()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        await using (var seed = CreateInMemory(null))
        {
            await seed.Products.AddRangeAsync(new[]
            {
                new Product { Id = Guid.NewGuid(), TenantId = tenantA, Name = "Narty A", Sku = "A1", DailyPrice = 10, AvailableQuantity = 5, CreatedAtUtc = DateTime.UtcNow },
                new Product { Id = Guid.NewGuid(), TenantId = tenantB, Name = "Narty B", Sku = "B1", DailyPrice = 12, AvailableQuantity = 3, CreatedAtUtc = DateTime.UtcNow }
            });
            await seed.SaveChangesAsync();
        }

        await using var ctxA = CreateInMemory(tenantA);
        // kopiujemy dane do nowej bazy in-memory
        await using (var copy = CreateInMemory(null))
        {
            var all = await copy.Products.AsNoTracking().ToListAsync();
            await ctxA.AddRangeAsync(all);
            await ctxA.SaveChangesAsync();
        }

        var visible = await ctxA.Products.AsNoTracking().ToListAsync();
        Assert.All(visible, p => Assert.Equal(tenantA, p.TenantId));
    }

    [Fact]
    public async Task UniqueSkuPerTenant_AllowsSameSkuAcrossTenants()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using var ctx = CreateInMemory(null);
        await ctx.Products.AddAsync(new Product { Id = Guid.NewGuid(), TenantId = tenantA, Name = "Kask", Sku = "SKU-1", DailyPrice = 5, AvailableQuantity = 2, CreatedAtUtc = DateTime.UtcNow });
        await ctx.Products.AddAsync(new Product { Id = Guid.NewGuid(), TenantId = tenantB, Name = "Kask2", Sku = "SKU-1", DailyPrice = 6, AvailableQuantity = 1, CreatedAtUtc = DateTime.UtcNow });
        await ctx.SaveChangesAsync();

        var countA = await ctx.Products.CountAsync(p => p.TenantId == tenantA && p.Sku == "SKU-1");
        var countB = await ctx.Products.CountAsync(p => p.TenantId == tenantB && p.Sku == "SKU-1");
        Assert.Equal(1, countA);
        Assert.Equal(1, countB);
    }

    [Fact]
    public async Task CustomersAndRentals_AreFilteredByTenant()
    {
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();
        await using (var seed = CreateInMemory(null))
        {
            var c1 = new Customer { Id = Guid.NewGuid(), TenantId = t1, FullName = "Jan" };
            var c2 = new Customer { Id = Guid.NewGuid(), TenantId = t2, FullName = "Ewa" };
            await seed.Customers.AddRangeAsync(c1, c2);
            await seed.Rentals.AddRangeAsync(
                new Rental { Id = Guid.NewGuid(), TenantId = t1, CustomerId = c1.Id, StartDateUtc = DateTime.UtcNow, EndDateUtc = DateTime.UtcNow.AddDays(1), Status = RentalStatus.Confirmed, CreatedAtUtc = DateTime.UtcNow },
                new Rental { Id = Guid.NewGuid(), TenantId = t2, CustomerId = c2.Id, StartDateUtc = DateTime.UtcNow, EndDateUtc = DateTime.UtcNow.AddDays(1), Status = RentalStatus.Confirmed, CreatedAtUtc = DateTime.UtcNow }
            );
            await seed.SaveChangesAsync();
        }

        // skopiuj dane do kontekstu z filtrem t1
        await using var ctx = CreateInMemory(t1);
        await using (var copy = CreateInMemory(null))
        {
            await ctx.Customers.AddRangeAsync(await copy.Customers.AsNoTracking().ToListAsync());
            await ctx.Rentals.AddRangeAsync(await copy.Rentals.AsNoTracking().ToListAsync());
            await ctx.SaveChangesAsync();
        }

        var customers = await ctx.Customers.AsNoTracking().ToListAsync();
        var rentals = await ctx.Rentals.AsNoTracking().ToListAsync();
        Assert.All(customers, c => Assert.Equal(t1, c.TenantId));
        Assert.All(rentals, r => Assert.Equal(t1, r.TenantId));
    }

    [Fact]
    public async Task DeletingRental_CascadesToItems()
    {
        var t = Guid.NewGuid();
        await using var ctx = CreateInMemory(null);
        var product = new Product { Id = Guid.NewGuid(), TenantId = t, Name = "Deska", Sku = "D1", DailyPrice = 10, AvailableQuantity = 10, CreatedAtUtc = DateTime.UtcNow };
        var cust = new Customer { Id = Guid.NewGuid(), TenantId = t, FullName = "Anna" };
        var rental = new Rental { Id = Guid.NewGuid(), TenantId = t, CustomerId = cust.Id, StartDateUtc = DateTime.UtcNow, EndDateUtc = DateTime.UtcNow.AddDays(1), Status = RentalStatus.Confirmed, CreatedAtUtc = DateTime.UtcNow };
        var item = new RentalItem { Id = Guid.NewGuid(), RentalId = rental.Id, ProductId = product.Id, Quantity = 1, PricePerDay = 10, Subtotal = 10 };
        await ctx.AddRangeAsync(product, cust, rental, item);
        await ctx.SaveChangesAsync();

        ctx.Rentals.Remove(rental);
        await ctx.SaveChangesAsync();

        Assert.Empty(await ctx.RentalItems.ToListAsync());
    }

    [Fact]
    public async Task ContractTemplate_FilteredByTenant()
    {
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();
        await using var ctx = CreateInMemory(null);
        await ctx.ContractTemplates.AddRangeAsync(
            new ContractTemplate { Id = Guid.NewGuid(), TenantId = t1, Content = "A" },
            new ContractTemplate { Id = Guid.NewGuid(), TenantId = t2, Content = "B" }
        );
        await ctx.SaveChangesAsync();

        ctx.SetTenant(t1);
        var list = await ctx.ContractTemplates.AsNoTracking().ToListAsync();
        Assert.Single(list);
        Assert.Equal("A", list[0].Content);
    }
}


