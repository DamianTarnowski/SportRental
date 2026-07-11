using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SportRental.Admin.Payments;
using SportRental.Admin.Services;
using SportRental.Admin.Services.Contracts;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;
using SportRental.Shared.Legal;
using SportRental.Shared.Models;

namespace SportRental.Admin.Tests.Payments;

public sealed class CheckoutFinalizationServiceTests
{
    [Fact]
    public async Task PersistRentalsAsync_CreatesOneOrderAndIndependentRentalPerTenant()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"checkout-finalization-{Guid.NewGuid():N}")
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var factory = new TestDbContextFactory(options);
        var checkoutId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var dailyTenantId = Guid.NewGuid();
        var hourlyTenantId = Guid.NewGuid();
        var dailyProductId = Guid.NewGuid();
        var hourlyProductId = Guid.NewGuid();
        var dailyStart = new DateTime(2026, 8, 1, 8, 0, 0, DateTimeKind.Utc);
        var dailyEnd = dailyStart.AddDays(2);
        var hourlyStart = new DateTime(2026, 8, 5, 9, 0, 0, DateTimeKind.Utc);
        var hourlyEnd = hourlyStart.AddHours(3);
        var payload = new CheckoutRentalPayload
        {
            SchemaVersion = 2,
            Customer = new CheckoutCustomerSnapshot
            {
                CustomerId = customerId,
                FullName = "Jan Klient",
                Email = "jan@example.test"
            },
            StartDateUtc = dailyStart,
            EndDateUtc = dailyEnd,
            IdempotencyKey = $"checkout-{checkoutId:N}",
            TotalAmount = 260m,
            DepositAmount = 78m,
            AcceptedTermsVersion = LegalDocumentVersions.Terms,
            AcknowledgedPrivacyVersion = LegalDocumentVersions.Privacy,
            Tenants =
            [
                new CheckoutTenantPayload
                {
                    Sequence = 1,
                    TenantId = dailyTenantId,
                    TenantName = "Alpine Rent",
                    StartDateUtc = dailyStart,
                    EndDateUtc = dailyEnd,
                    RentalType = RentalTypeDto.Daily,
                    TotalAmount = 200m,
                    DepositAmount = 60m,
                    RegulationsTextSnapshot = "Regulamin Alpine",
                    RegulationsHash = "hash-alpine",
                    RegulationsVersion = "tenant-alpine",
                    RegulationsSource = "TenantCustom",
                    Items =
                    [
                        new CheckoutRentalItemPayload
                        {
                            ProductId = dailyProductId,
                            Quantity = 1,
                            PricePerDay = 100m,
                            Subtotal = 200m
                        }
                    ]
                },
                new CheckoutTenantPayload
                {
                    Sequence = 2,
                    TenantId = hourlyTenantId,
                    TenantName = "Bike Point",
                    StartDateUtc = hourlyStart,
                    EndDateUtc = hourlyEnd,
                    RentalType = RentalTypeDto.Hourly,
                    HoursRented = 3,
                    TotalAmount = 60m,
                    DepositAmount = 18m,
                    RegulationsTextSnapshot = "Regulamin standardowy",
                    RegulationsHash = "hash-standard",
                    RegulationsVersion = "standard-v1",
                    RegulationsSource = "PlatformDefault",
                    Items =
                    [
                        new CheckoutRentalItemPayload
                        {
                            ProductId = hourlyProductId,
                            Quantity = 1,
                            PricePerDay = 120m,
                            PricePerHour = 20m,
                            Subtotal = 60m
                        }
                    ]
                }
            ]
        };

        await using (var db = new ApplicationDbContext(options))
        {
            db.Tenants.AddRange(
                new Tenant { Id = dailyTenantId, Name = "Alpine Rent" },
                new Tenant { Id = hourlyTenantId, Name = "Bike Point" });
            db.Customers.Add(new Customer
            {
                Id = customerId,
                TenantId = Guid.Empty,
                FullName = "Jan Klient",
                Email = "jan@example.test"
            });
            db.Products.AddRange(
                Product(dailyProductId, dailyTenantId, "Narty", 100m, null),
                Product(hourlyProductId, hourlyTenantId, "Rower", 120m, 20m));
            db.CheckoutSessions.Add(new CheckoutSession
            {
                Id = checkoutId,
                IdempotencyKey = payload.IdempotencyKey,
                PayloadJson = JsonSerializer.Serialize(payload),
                CreatedAtUtc = DateTime.UtcNow,
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(30),
                AcceptedTermsVersion = LegalDocumentVersions.Terms,
                AcknowledgedPrivacyVersion = LegalDocumentVersions.Privacy,
                LegalAcceptedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var service = new CheckoutFinalizationService(
            factory,
            Mock.Of<IContractGenerator>(),
            Mock.Of<IRentalConfirmationService>(),
            Mock.Of<IPaymentGateway>(),
            Options.Create(new StripeOptions()),
            NullLogger<CheckoutFinalizationService>.Instance);

        var result = await service.PersistRentalsAsync(
            checkoutId,
            "cs_test_marketplace",
            "pi_test_marketplace",
            7_800,
            "pln",
            CancellationToken.None);

        // Późny retry redirectu/webhooka nie może wskrzesić zamówienia, które
        // zostało już anulowane i rozliczone po pierwszej finalizacji.
        await using (var cancellation = new ApplicationDbContext(options))
        {
            var cancelledOrder = await cancellation.MarketplaceOrders.SingleAsync();
            cancelledOrder.Status = "Cancelled";
            cancelledOrder.PaymentStatus = "Refunded";
            cancelledOrder.RefundedDepositAmount = cancelledOrder.DepositAmount;
            await cancellation.SaveChangesAsync();
        }

        var retryResult = await service.PersistRentalsAsync(
            checkoutId,
            "cs_test_marketplace",
            "pi_test_marketplace",
            7_800,
            "pln",
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.AlreadyExisted.Should().BeFalse();
        result.MarketplaceOrderId.Should().NotBeNull();
        result.MarketplaceOrderNumber.Should().StartWith("RS-");
        result.RentalIds.Should().HaveCount(2);
        retryResult.Success.Should().BeTrue();
        retryResult.AlreadyExisted.Should().BeTrue();
        retryResult.RentalIds.Should().BeEquivalentTo(result.RentalIds);

        await using var verification = new ApplicationDbContext(options);
        var order = await verification.MarketplaceOrders
            .Include(candidate => candidate.Rentals)
            .SingleAsync();
        order.TotalAmount.Should().Be(260m);
        order.DepositAmount.Should().Be(78m);
        order.CustomerEmailSnapshot.Should().Be("jan@example.test");
        order.Status.Should().Be("Cancelled");
        order.PaymentStatus.Should().Be("Refunded");
        order.RefundedDepositAmount.Should().Be(78m);
        order.Rentals.Should().HaveCount(2);

        var daily = order.Rentals.Single(rental => rental.TenantId == dailyTenantId);
        daily.OrderSequence.Should().Be(1);
        daily.StartDateUtc.Should().Be(dailyStart);
        daily.EndDateUtc.Should().Be(dailyEnd);
        daily.RentalType.Should().Be(RentalType.Daily);
        daily.RegulationsTextSnapshot.Should().Be("Regulamin Alpine");
        daily.RegulationsHash.Should().Be("hash-alpine");

        var hourly = order.Rentals.Single(rental => rental.TenantId == hourlyTenantId);
        hourly.OrderSequence.Should().Be(2);
        hourly.StartDateUtc.Should().Be(hourlyStart);
        hourly.EndDateUtc.Should().Be(hourlyEnd);
        hourly.RentalType.Should().Be(RentalType.Hourly);
        hourly.HoursRented.Should().Be(3);
        hourly.RegulationsSource.Should().Be("PlatformDefault");
    }

    private static Product Product(
        Guid id,
        Guid tenantId,
        string name,
        decimal dailyPrice,
        decimal? hourlyPrice) => new()
    {
        Id = id,
        TenantId = tenantId,
        Name = name,
        Sku = $"TEST-{id:N}",
        DailyPrice = dailyPrice,
        HourlyPrice = hourlyPrice,
        IsActive = true,
        Available = true,
        AvailableQuantity = 5,
        CreatedAtUtc = DateTime.UtcNow
    };

    private sealed class TestDbContextFactory(
        DbContextOptions<ApplicationDbContext> options) : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);

        public Task<ApplicationDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
