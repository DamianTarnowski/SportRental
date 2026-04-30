using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SportRental.Admin.Tests.Integration.Infrastructure;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;
using Xunit;

namespace SportRental.Admin.Tests.Integration;

/// <summary>
/// Weryfikuje że TenantId query filter w ApplicationDbContext skutecznie ukrywa dane
/// pomiędzy tenantami. Każdy z testów tworzy 2 tenanty (A i B), zaseeduje po stronie
/// obu, ustawia SetTenant(A) i sprawdza że dane B nie wyciekają.
/// </summary>
[Collection("postgres")]
[Trait("Category", "RequiresDocker")]
public class MultiTenancyTests
{
    private readonly PostgresFixture _pg;

    public MultiTenancyTests(PostgresFixture pg) => _pg = pg;

    [Fact]
    public async Task QueryFilter_OnProducts_HidesOtherTenantRows()
    {
        await _pg.ResetDataAsync();
        var (tenantA, tenantB) = await SeedTwoTenantsAsync();

        await using (var seedCtx = _pg.CreateDbContext())
        {
            seedCtx.Products.AddRange(
                NewProduct(tenantA, "Rower A"),
                NewProduct(tenantA, "Narty A"),
                NewProduct(tenantB, "Rower B"));
            await seedCtx.SaveChangesAsync();
        }

        await using var ctx = _pg.CreateDbContext();
        ctx.SetTenant(tenantA);
        var visibleProducts = await ctx.Products.OrderBy(p => p.Name).ToListAsync();

        visibleProducts.Should().HaveCount(2);
        visibleProducts.Select(p => p.Name).Should().BeEquivalentTo(new[] { "Narty A", "Rower A" });
        visibleProducts.Should().NotContain(p => p.Name == "Rower B");
    }

    [Fact]
    public async Task QueryFilter_OnCustomers_HidesOtherTenantRows()
    {
        await _pg.ResetDataAsync();
        var (tenantA, tenantB) = await SeedTwoTenantsAsync();

        await using (var seedCtx = _pg.CreateDbContext())
        {
            seedCtx.Customers.AddRange(
                new Customer { Id = Guid.NewGuid(), TenantId = tenantA, FullName = "Anna A", Email = "a@x.pl" },
                new Customer { Id = Guid.NewGuid(), TenantId = tenantB, FullName = "Bartek B", Email = "b@x.pl" });
            await seedCtx.SaveChangesAsync();
        }

        await using var ctx = _pg.CreateDbContext();
        ctx.SetTenant(tenantA);
        var visible = await ctx.Customers.ToListAsync();

        visible.Should().ContainSingle(c => c.FullName == "Anna A");
        visible.Should().NotContain(c => c.FullName == "Bartek B");
    }

    [Fact]
    public async Task QueryFilter_OnRentals_HidesOtherTenantRows()
    {
        await _pg.ResetDataAsync();
        var (tenantA, tenantB) = await SeedTwoTenantsAsync();

        var customerA = Guid.NewGuid();
        var customerB = Guid.NewGuid();
        await using (var seedCtx = _pg.CreateDbContext())
        {
            seedCtx.Customers.AddRange(
                new Customer { Id = customerA, TenantId = tenantA, FullName = "Anna A" },
                new Customer { Id = customerB, TenantId = tenantB, FullName = "Bartek B" });
            seedCtx.Rentals.AddRange(
                new Rental { Id = Guid.NewGuid(), TenantId = tenantA, CustomerId = customerA,
                    StartDateUtc = DateTime.UtcNow, EndDateUtc = DateTime.UtcNow.AddDays(1),
                    Status = RentalStatus.Active, TotalAmount = 100m },
                new Rental { Id = Guid.NewGuid(), TenantId = tenantB, CustomerId = customerB,
                    StartDateUtc = DateTime.UtcNow, EndDateUtc = DateTime.UtcNow.AddDays(1),
                    Status = RentalStatus.Active, TotalAmount = 200m });
            await seedCtx.SaveChangesAsync();
        }

        await using var ctx = _pg.CreateDbContext();
        ctx.SetTenant(tenantB);
        var bRentals = await ctx.Rentals.ToListAsync();

        bRentals.Should().ContainSingle();
        bRentals[0].TotalAmount.Should().Be(200m);
        bRentals[0].TenantId.Should().Be(tenantB);
    }

    [Fact]
    public async Task NoTenantSet_SeesEverything_UseWithCare()
    {
        // Filter jest `TenantId == null || p.TenantId == TenantId`. Bez SetTenant
        // (TenantId == null) widzimy wszystko — to świadoma furtka dla SuperAdmin/admin
        // operacji cross-tenant. Test pilnuje że ten kontrakt się nie zmienił przypadkiem.
        await _pg.ResetDataAsync();
        var (tenantA, tenantB) = await SeedTwoTenantsAsync();

        await using (var seedCtx = _pg.CreateDbContext())
        {
            seedCtx.Products.AddRange(
                NewProduct(tenantA, "P-A"),
                NewProduct(tenantB, "P-B"));
            await seedCtx.SaveChangesAsync();
        }

        await using var ctx = _pg.CreateDbContext();
        // Brak SetTenant
        var all = await ctx.Products.ToListAsync();

        all.Should().HaveCount(2);
        all.Select(p => p.Name).Should().BeEquivalentTo(new[] { "P-A", "P-B" });
    }

    [Fact]
    public async Task IgnoreQueryFilters_ReturnsAllTenantsRows()
    {
        // Niektóre serwisy (background jobs, super-admin reports) używają
        // .IgnoreQueryFilters() świadomie. Test pilnuje że ta ścieżka działa.
        await _pg.ResetDataAsync();
        var (tenantA, tenantB) = await SeedTwoTenantsAsync();

        await using (var seedCtx = _pg.CreateDbContext())
        {
            seedCtx.Products.AddRange(
                NewProduct(tenantA, "Visible A"),
                NewProduct(tenantB, "Hidden B"));
            await seedCtx.SaveChangesAsync();
        }

        await using var ctx = _pg.CreateDbContext();
        ctx.SetTenant(tenantA);

        var withFilter = await ctx.Products.ToListAsync();
        var withoutFilter = await ctx.Products.IgnoreQueryFilters().ToListAsync();

        withFilter.Should().ContainSingle(p => p.Name == "Visible A");
        withoutFilter.Should().HaveCount(2);
    }

    private async Task<(Guid tenantA, Guid tenantB)> SeedTwoTenantsAsync()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        await using var ctx = _pg.CreateDbContext();
        ctx.Tenants.Add(new Tenant { Id = tenantA, Name = "Tenant A" });
        ctx.Tenants.Add(new Tenant { Id = tenantB, Name = "Tenant B" });
        await ctx.SaveChangesAsync();
        return (tenantA, tenantB);
    }

    private static Product NewProduct(Guid tenantId, string name) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        Name = name,
        Sku = $"SKU-{Guid.NewGuid():N}",
        DailyPrice = 50m,
        AvailableQuantity = 5,
        IsActive = true,
        Available = true,
        CreatedAtUtc = DateTime.UtcNow
    };
}
