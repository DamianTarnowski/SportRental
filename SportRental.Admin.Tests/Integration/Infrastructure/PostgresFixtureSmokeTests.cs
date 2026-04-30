using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SportRental.Infrastructure.Domain;
using Xunit;

namespace SportRental.Admin.Tests.Integration.Infrastructure;

[Collection("postgres")]
[Trait("Category", "RequiresDocker")]
public class PostgresFixtureSmokeTests
{
    private readonly PostgresFixture _pg;

    public PostgresFixtureSmokeTests(PostgresFixture pg) => _pg = pg;

    [Fact]
    public async Task Fixture_CanInsertAndQueryTenant()
    {
        await _pg.ResetDataAsync();

        var tenantId = Guid.NewGuid();
        await using (var ctx = _pg.CreateDbContext())
        {
            ctx.Tenants.Add(new Tenant { Id = tenantId, Name = "Smoke Tenant" });
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = _pg.CreateDbContext())
        {
            var found = await ctx.Tenants.SingleAsync(t => t.Id == tenantId);
            found.Name.Should().Be("Smoke Tenant");
        }
    }

    [Fact]
    public async Task Reset_ClearsRowsBetweenTests()
    {
        await _pg.ResetDataAsync();

        await using (var ctx = _pg.CreateDbContext())
        {
            ctx.Tenants.Add(new Tenant { Id = Guid.NewGuid(), Name = "Pre-reset" });
            await ctx.SaveChangesAsync();
        }

        await _pg.ResetDataAsync();

        await using (var ctx = _pg.CreateDbContext())
        {
            (await ctx.Tenants.CountAsync()).Should().Be(0);
        }
    }
}
