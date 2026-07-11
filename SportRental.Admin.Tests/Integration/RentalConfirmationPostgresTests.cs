using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using SportRental.Admin.Hubs;
using SportRental.Admin.Services;
using SportRental.Admin.Services.Email;
using SportRental.Admin.Services.Sms;
using SportRental.Admin.Tests.Integration.Infrastructure;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;
using SportRental.Infrastructure.Tenancy;

namespace SportRental.Admin.Tests.Integration;

[Collection("postgres")]
[Trait("Category", "RequiresDocker")]
public class RentalConfirmationPostgresTests
{
    private readonly PostgresFixture _postgres;

    public RentalConfirmationPostgresTests(PostgresFixture postgres) => _postgres = postgres;

    [Fact]
    public async Task LinkConfirmation_PersistsProofAndRentalStatus_InPostgres()
    {
        await _postgres.ResetDataAsync();
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var rentalId = Guid.NewGuid();

        await using (var db = _postgres.CreateDbContext())
        {
            db.Tenants.Add(new Tenant { Id = tenantId, Name = "Postgres Rental" });
            db.CompanyInfos.Add(new CompanyInfo
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Name = "Postgres Rental",
                RegulationsText = "Postgres test regulations"
            });
            db.Customers.Add(new Customer
            {
                Id = customerId,
                TenantId = tenantId,
                FullName = "Jan PostgreSQL",
                Email = "jan-postgres@example.test",
                PhoneNumber = "+48123123123"
            });
            db.Products.Add(new Product
            {
                Id = productId,
                TenantId = tenantId,
                Name = "Rower PostgreSQL",
                Sku = "PG-TEST-1",
                DailyPrice = 75,
                AvailableQuantity = 1
            });
            db.Rentals.Add(new Rental
            {
                Id = rentalId,
                TenantId = tenantId,
                CustomerId = customerId,
                StartDateUtc = DateTime.UtcNow.AddDays(1),
                EndDateUtc = DateTime.UtcNow.AddDays(2),
                TotalAmount = 75,
                DepositAmount = 100,
                Status = RentalStatus.Pending
            });
            db.RentalItems.Add(new RentalItem
            {
                Id = Guid.NewGuid(),
                RentalId = rentalId,
                ProductId = productId,
                Quantity = 1,
                PricePerDay = 75,
                Subtotal = 75
            });
            await db.SaveChangesAsync();
        }

        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(x => x.GetCurrentTenantId()).Returns(tenantId);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["App:BaseUrl"] = "https://app.example.test"
            })
            .Build();
        var service = new RentalConfirmationService(
            new PostgresDbContextFactory(_postgres),
            tenantProvider.Object,
            Mock.Of<ILogger<RentalConfirmationService>>(),
            Mock.Of<ISmsSender>(),
            Mock.Of<IRentalNotificationService>(),
            configuration,
            Mock.Of<IEmailSender>());

        var token = await service.CreateConfirmationAsync(rentalId);
        var page = await service.GetConfirmationDataAsync(token);
        var result = await service.ProcessConfirmationAsync(
            token,
            "203.0.113.20",
            "postgres-integration-test");

        token.Should().HaveLength(43);
        page.Should().NotBeNull();
        page!.Items.Should().ContainSingle(i => i.ProductName == "Rower PostgreSQL");
        result.Success.Should().BeTrue();

        await using var verifyDb = _postgres.CreateDbContext();
        verifyDb.SetTenant(tenantId);
        var rental = await verifyDb.Rentals.SingleAsync(r => r.Id == rentalId);
        var confirmation = await verifyDb.RentalConfirmations.SingleAsync(c => c.Token == token);

        rental.Status.Should().Be(RentalStatus.Confirmed);
        rental.IsSmsConfirmed.Should().BeTrue();
        confirmation.IsConfirmed.Should().BeTrue();
        confirmation.ConfirmedFromIp.Should().Be("203.0.113.20");
        confirmation.ConfirmedUserAgent.Should().Be("postgres-integration-test");
        confirmation.RegulationsHash.Should().HaveLength(64);
    }

    private sealed class PostgresDbContextFactory(PostgresFixture postgres)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => postgres.CreateDbContext();

        public Task<ApplicationDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
