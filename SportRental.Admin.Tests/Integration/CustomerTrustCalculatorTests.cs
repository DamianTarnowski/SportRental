using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SportRental.Admin.Services;
using SportRental.Admin.Tests.Integration.Infrastructure;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;
using Xunit;

namespace SportRental.Admin.Tests.Integration;

/// <summary>
/// Reguły CustomerTrustCalculator (vide klasa):
///   Restricted — średnia &lt; 5.0 (przy &gt;0 ocen), 3+ incydentów (jakikolwiek score &lt; 5),
///                lub manualny override.
///   Watch      — 1-2 incydenty albo średnia 5.0-8.0 przy &gt;= 10 ocenach.
///   Good       — &gt;= 10 ocen, średnia ≥ 8.0, 0 incydentów.
///   Unverified — &lt; 10 ocen i brak red flagów.
/// </summary>
[Collection("postgres")]
[Trait("Category", "RequiresDocker")]
public class CustomerTrustCalculatorTests
{
    private readonly PostgresFixture _pg;

    public CustomerTrustCalculatorTests(PostgresFixture pg) => _pg = pg;

    [Fact]
    public async Task NewCustomer_NoReviews_RemainsUnverified()
    {
        await _pg.ResetDataAsync();
        var (tenantId, customerId) = await SeedTenantAndCustomerAsync();

        await CreateCalculator().RecalculateAsync(customerId);

        var customer = await ReloadCustomerAsync(customerId);
        customer.TrustLevel.Should().Be(CustomerTrustLevel.Unverified);
        customer.TrustCompletedRentalsCount.Should().Be(0);
        customer.TrustIncidentCount.Should().Be(0);
    }

    [Fact]
    public async Task TenPerfectReviews_PromotesToGood()
    {
        await _pg.ResetDataAsync();
        var (tenantId, customerId) = await SeedTenantAndCustomerAsync();
        await SeedReviewsAsync(tenantId, customerId, count: 10, timeliness: 10, condition: 10, communication: 10);

        await CreateCalculator().RecalculateAsync(customerId);

        var customer = await ReloadCustomerAsync(customerId);
        customer.TrustLevel.Should().Be(CustomerTrustLevel.Good);
        customer.TrustAverageScore.Should().Be(10.0);
        customer.TrustIncidentCount.Should().Be(0);
    }

    [Fact]
    public async Task SingleIncident_KeepsCustomerWatch_NotRestricted()
    {
        await _pg.ResetDataAsync();
        var (tenantId, customerId) = await SeedTenantAndCustomerAsync();
        // 9 dobrych + 1 incydent (ConditionScore = 3) = razem 10 ocen, 1 incydent.
        await SeedReviewsAsync(tenantId, customerId, count: 9, timeliness: 9, condition: 9, communication: 9);
        await SeedSingleReviewAsync(tenantId, customerId, timeliness: 9, condition: 3, communication: 9);

        await CreateCalculator().RecalculateAsync(customerId);

        var customer = await ReloadCustomerAsync(customerId);
        customer.TrustLevel.Should().Be(CustomerTrustLevel.Watch);
        customer.TrustIncidentCount.Should().Be(1);
    }

    [Fact]
    public async Task ThreeIncidents_PromotesToRestricted()
    {
        await _pg.ResetDataAsync();
        var (tenantId, customerId) = await SeedTenantAndCustomerAsync();
        await SeedReviewsAsync(tenantId, customerId, count: 7, timeliness: 9, condition: 9, communication: 9);
        await SeedSingleReviewAsync(tenantId, customerId, timeliness: 3, condition: 9, communication: 9);
        await SeedSingleReviewAsync(tenantId, customerId, timeliness: 9, condition: 4, communication: 9);
        await SeedSingleReviewAsync(tenantId, customerId, timeliness: 9, condition: 9, communication: 2);

        await CreateCalculator().RecalculateAsync(customerId);

        var customer = await ReloadCustomerAsync(customerId);
        customer.TrustLevel.Should().Be(CustomerTrustLevel.Restricted);
        customer.TrustIncidentCount.Should().Be(3);
    }

    [Fact]
    public async Task LowAverageBelow5_ForcesRestricted()
    {
        await _pg.ResetDataAsync();
        var (tenantId, customerId) = await SeedTenantAndCustomerAsync();
        // Wszystkie 4 — średnia 4.0 < 5.0
        await SeedReviewsAsync(tenantId, customerId, count: 5, timeliness: 4, condition: 4, communication: 4);

        await CreateCalculator().RecalculateAsync(customerId);

        var customer = await ReloadCustomerAsync(customerId);
        customer.TrustLevel.Should().Be(CustomerTrustLevel.Restricted);
        customer.TrustAverageScore.Should().Be(4.0);
    }

    [Fact]
    public async Task ManualOverride_TrumpsAutomaticCalculation()
    {
        await _pg.ResetDataAsync();
        var (tenantId, customerId) = await SeedTenantAndCustomerAsync();
        // Doskonałe oceny by automat dał Good, ale admin manualnie zablokował.
        await SeedReviewsAsync(tenantId, customerId, count: 10, timeliness: 10, condition: 10, communication: 10);

        await using (var ctx = _pg.CreateDbContext())
        {
            var customer = await ctx.Customers.IgnoreQueryFilters().SingleAsync(c => c.Id == customerId);
            customer.TrustLevelManualOverride = CustomerTrustLevel.Restricted;
            customer.TrustLevelManualReason = "Admin block: rzucał klientami sprzętem";
            await ctx.SaveChangesAsync();
        }

        await CreateCalculator().RecalculateAsync(customerId);

        var reloaded = await ReloadCustomerAsync(customerId);
        reloaded.TrustLevel.Should().Be(CustomerTrustLevel.Restricted);
    }

    [Fact]
    public async Task CrossTenantReviews_AllAggregated()
    {
        await _pg.ResetDataAsync();
        var (tenantA, customerId) = await SeedTenantAndCustomerAsync();
        var tenantB = Guid.NewGuid();
        await using (var ctx = _pg.CreateDbContext())
        {
            ctx.Tenants.Add(new Tenant { Id = tenantB, Name = "Tenant B" });
            await ctx.SaveChangesAsync();
        }

        // 6 z tenant A + 6 z tenant B = 12 łącznie -> qualifies for Good.
        await SeedReviewsAsync(tenantA, customerId, count: 6, timeliness: 9, condition: 9, communication: 9);
        await SeedReviewsAsync(tenantB, customerId, count: 6, timeliness: 9, condition: 9, communication: 9);

        await CreateCalculator().RecalculateAsync(customerId);

        var customer = await ReloadCustomerAsync(customerId);
        customer.TrustCompletedRentalsCount.Should().Be(12);
        customer.TrustLevel.Should().Be(CustomerTrustLevel.Good);
    }

    private CustomerTrustCalculator CreateCalculator()
    {
        return new CustomerTrustCalculator(new SimpleDbContextFactory(_pg));
    }

    private async Task<(Guid tenantId, Guid customerId)> SeedTenantAndCustomerAsync()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        await using var ctx = _pg.CreateDbContext();
        ctx.Tenants.Add(new Tenant { Id = tenantId, Name = "Tenant A" });
        ctx.Customers.Add(new Customer
        {
            Id = customerId,
            TenantId = tenantId,
            FullName = "Jan Test",
            Email = "jan@test.pl",
            CreatedAtUtc = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
        return (tenantId, customerId);
    }

    private async Task SeedReviewsAsync(Guid tenantId, Guid customerId, int count, int timeliness, int condition, int communication)
    {
        await using var ctx = _pg.CreateDbContext();
        for (int i = 0; i < count; i++)
        {
            var rentalId = Guid.NewGuid();
            ctx.Rentals.Add(new Rental
            {
                Id = rentalId, TenantId = tenantId, CustomerId = customerId,
                StartDateUtc = DateTime.UtcNow.AddDays(-30), EndDateUtc = DateTime.UtcNow.AddDays(-29),
                Status = RentalStatus.Completed, TotalAmount = 50m
            });
            ctx.CustomerReviews.Add(new CustomerReview
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                CustomerId = customerId,
                RentalId = rentalId,
                TimelinessScore = timeliness,
                ConditionScore = condition,
                CommunicationScore = communication,
                CreatedAtUtc = DateTime.UtcNow
            });
        }
        await ctx.SaveChangesAsync();
    }

    private async Task SeedSingleReviewAsync(Guid tenantId, Guid customerId, int timeliness, int condition, int communication)
        => await SeedReviewsAsync(tenantId, customerId, count: 1,
            timeliness: timeliness, condition: condition, communication: communication);

    private async Task<Customer> ReloadCustomerAsync(Guid customerId)
    {
        await using var ctx = _pg.CreateDbContext();
        return await ctx.Customers.IgnoreQueryFilters().SingleAsync(c => c.Id == customerId);
    }

    private sealed class SimpleDbContextFactory : IDbContextFactory<ApplicationDbContext>
    {
        private readonly PostgresFixture _pg;
        public SimpleDbContextFactory(PostgresFixture pg) => _pg = pg;
        public ApplicationDbContext CreateDbContext() => _pg.CreateDbContext();
    }
}
