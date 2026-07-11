using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using SportRental.Admin.Hubs;
using SportRental.Admin.Services;
using SportRental.Admin.Services.Email;
using SportRental.Admin.Services.Sms;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;
using SportRental.Infrastructure.Tenancy;

namespace SportRental.Admin.Tests.Services;

public class RentalConfirmationServiceTests
{
    [Fact]
    public async Task ProcessConfirmation_WithValidToken_StoresProofAndConfirmsRental()
    {
        var fixture = await CreateFixtureAsync();
        var token = await fixture.Service.CreateConfirmationAsync(fixture.RentalId);
        var longUserAgent = new string('a', 600);

        var result = await fixture.Service.ProcessConfirmationAsync(
            token,
            "203.0.113.10",
            longUserAgent);

        result.Success.Should().BeTrue();

        await using var db = fixture.Factory.CreateDbContext();
        db.SetTenant(fixture.TenantId);
        var rental = await db.Rentals.SingleAsync(r => r.Id == fixture.RentalId);
        var confirmation = await db.RentalConfirmations.SingleAsync(r => r.Token == token);

        rental.Status.Should().Be(RentalStatus.Confirmed);
        rental.IsSmsConfirmed.Should().BeTrue();
        confirmation.IsConfirmed.Should().BeTrue();
        confirmation.ConfirmedAt.Should().NotBeNull();
        confirmation.ConfirmedFromIp.Should().Be("203.0.113.10");
        confirmation.ConfirmedUserAgent.Should().HaveLength(500);
        confirmation.RegulationsHash.Should().HaveLength(64);

        var page = await fixture.Service.GetConfirmationDataAsync(token);
        page.Should().NotBeNull();
        page!.IsAlreadyConfirmed.Should().BeTrue();
    }

    [Fact]
    public async Task ProcessConfirmation_WithExpiredToken_DoesNotConfirmRental()
    {
        var fixture = await CreateFixtureAsync();
        var token = await fixture.Service.CreateConfirmationAsync(fixture.RentalId);

        await using (var db = fixture.Factory.CreateDbContext())
        {
            db.SetTenant(fixture.TenantId);
            var confirmation = await db.RentalConfirmations.SingleAsync(r => r.Token == token);
            confirmation.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        var result = await fixture.Service.ProcessConfirmationAsync(token, "203.0.113.10", "test-agent");

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("wygasł");

        await using var verifyDb = fixture.Factory.CreateDbContext();
        verifyDb.SetTenant(fixture.TenantId);
        var rental = await verifyDb.Rentals.SingleAsync(r => r.Id == fixture.RentalId);
        var storedConfirmation = await verifyDb.RentalConfirmations.SingleAsync(r => r.Token == token);
        rental.Status.Should().Be(RentalStatus.Pending);
        rental.IsSmsConfirmed.Should().BeFalse();
        storedConfirmation.IsConfirmed.Should().BeFalse();
    }

    [Fact]
    public async Task SendConfirmationLink_SmsExplainsActionAndContainsLink()
    {
        var fixture = await CreateFixtureAsync();
        string? sentMessage = null;
        string? sentEmailBody = null;
        fixture.Sms
            .Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, message, _) => sentMessage = message)
            .Returns(Task.CompletedTask);
        fixture.Email
            .Setup(x => x.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string, string>((_, _, body) => sentEmailBody = body)
            .Returns(Task.CompletedTask);

        var token = await fixture.Service.CreateConfirmationAsync(fixture.RentalId);
        var result = await fixture.Service.SendConfirmationLinkAsync(fixture.RentalId, token);

        result.SmsSent.Should().BeTrue();
        sentMessage.Should().StartWith("Kliknij w link, aby potwierdzic wynajem");
        sentMessage.Should().Contain($"https://app.example.test/confirm/{token}");
        sentMessage.Should().NotContain("Numer zamowienia potrzebny do odzyskania dostepu");
        sentEmailBody.Should().NotContain("Numer zamówienia:");
        sentEmailBody.Should().NotContain("odzyskania dostępu do zamówienia bez logowania");
    }

    [Fact]
    public async Task SendConfirmationLink_ForMarketplaceRental_IncludesSharedOrderNumberInSmsAndEmail()
    {
        const string orderNumber = "RS-20260710-ABC12345";
        var fixture = await CreateFixtureAsync(marketplaceOrderNumber: orderNumber);
        string? sentSms = null;
        string? sentEmailBody = null;
        fixture.Sms
            .Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, message, _) => sentSms = message)
            .Returns(Task.CompletedTask);
        fixture.Email
            .Setup(x => x.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string, string>((_, _, body) => sentEmailBody = body)
            .Returns(Task.CompletedTask);

        var token = await fixture.Service.CreateConfirmationAsync(fixture.RentalId);
        var result = await fixture.Service.SendConfirmationLinkAsync(fixture.RentalId, token);

        result.SmsSent.Should().BeTrue();
        result.EmailSent.Should().BeTrue();
        sentSms.Should().Contain(orderNumber);
        sentSms.Should().Contain("Numer zamowienia potrzebny do odzyskania dostepu");
        sentEmailBody.Should().Contain($"Numer zamówienia:</b> {orderNumber}");
        sentEmailBody.Should().Contain("potrzebny do odzyskania dostępu do zamówienia bez logowania");
    }

    [Fact]
    public async Task SendConfirmationLink_WhenSmsFailsAndEmailSucceeds_TracksOnlyEmail()
    {
        var fixture = await CreateFixtureAsync();
        fixture.Sms
            .Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SMS unavailable"));
        fixture.Email
            .Setup(x => x.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var token = await fixture.Service.CreateConfirmationAsync(fixture.RentalId);
        var result = await fixture.Service.SendConfirmationLinkAsync(fixture.RentalId, token);

        result.SmsAttempted.Should().BeTrue();
        result.SmsSent.Should().BeFalse();
        result.EmailAttempted.Should().BeTrue();
        result.EmailSent.Should().BeTrue();
        result.AnySent.Should().BeTrue();
        result.AllAttemptedSent.Should().BeFalse();

        await using var db = fixture.Factory.CreateDbContext();
        db.SetTenant(fixture.TenantId);
        var rental = await db.Rentals.SingleAsync(r => r.Id == fixture.RentalId);
        var confirmation = await db.RentalConfirmations.SingleAsync(r => r.Token == token);
        rental.IsSmsConfirmationSent.Should().BeFalse();
        confirmation.IsSmsSent.Should().BeFalse();
        confirmation.IsEmailSent.Should().BeTrue();
    }

    [Fact]
    public async Task SendConfirmationLink_WhenBothChannelsFail_DoesNotReportOrPersistSuccess()
    {
        var fixture = await CreateFixtureAsync();
        fixture.Sms
            .Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SMS unavailable"));
        fixture.Email
            .Setup(x => x.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("Email unavailable"));

        var token = await fixture.Service.CreateConfirmationAsync(fixture.RentalId);
        var result = await fixture.Service.SendConfirmationLinkAsync(fixture.RentalId, token);

        result.AnySent.Should().BeFalse();
        result.AllAttemptedSent.Should().BeFalse();

        await using var db = fixture.Factory.CreateDbContext();
        db.SetTenant(fixture.TenantId);
        var rental = await db.Rentals.SingleAsync(r => r.Id == fixture.RentalId);
        var confirmation = await db.RentalConfirmations.SingleAsync(r => r.Token == token);
        rental.IsSmsConfirmationSent.Should().BeFalse();
        confirmation.IsSmsSent.Should().BeFalse();
        confirmation.IsEmailSent.Should().BeFalse();
    }

    [Fact]
    public async Task ExplicitTenantMethods_WorkWithoutAmbientTenantContext()
    {
        var fixture = await CreateFixtureAsync(ambientTenant: false);
        fixture.Sms
            .Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        fixture.Email
            .Setup(x => x.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var token = await fixture.Service.CreateConfirmationForTenantAsync(
            fixture.TenantId, fixture.RentalId);
        var result = await fixture.Service.SendConfirmationLinkForTenantAsync(
            fixture.TenantId, fixture.RentalId, token);

        result.SmsSent.Should().BeTrue();
        result.EmailSent.Should().BeTrue();
    }

    [Fact]
    public async Task SendConfirmationLink_WhenRetried_DoesNotSendEitherChannelTwice()
    {
        var fixture = await CreateFixtureAsync();
        fixture.Sms
            .Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        fixture.Email
            .Setup(x => x.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var token = await fixture.Service.CreateConfirmationAsync(fixture.RentalId);
        var first = await fixture.Service.SendConfirmationLinkAsync(fixture.RentalId, token);
        var retry = await fixture.Service.SendConfirmationLinkAsync(fixture.RentalId, token);

        first.AnySent.Should().BeTrue();
        retry.AnySent.Should().BeTrue();
        fixture.Sms.Verify(
            x => x.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
        fixture.Email.Verify(
            x => x.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Once);
    }

    private static async Task<Fixture> CreateFixtureAsync(
        bool ambientTenant = true,
        string? marketplaceOrderNumber = null)
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var rentalId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var factory = new TestDbContextFactory();

        await using (var db = factory.CreateDbContext())
        {
            db.SetTenant(tenantId);
            db.Tenants.Add(new Tenant { Id = tenantId, Name = "Test Rental" });
            db.CompanyInfos.Add(new CompanyInfo
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Name = "Test Rental",
                RegulationsText = "Test regulations"
            });
            db.Customers.Add(new Customer
            {
                Id = customerId,
                TenantId = tenantId,
                FullName = "Jan Kowalski",
                Email = "jan@example.test",
                PhoneNumber = "+48123456789"
            });
            db.Products.Add(new Product
            {
                Id = productId,
                TenantId = tenantId,
                Name = "Rower testowy",
                Sku = "TEST-1",
                DailyPrice = 50,
                AvailableQuantity = 1
            });
            Guid? marketplaceOrderId = null;
            if (!string.IsNullOrWhiteSpace(marketplaceOrderNumber))
            {
                marketplaceOrderId = Guid.NewGuid();
                db.MarketplaceOrders.Add(new MarketplaceOrder
                {
                    Id = marketplaceOrderId.Value,
                    OrderNumber = marketplaceOrderNumber,
                    CustomerId = customerId,
                    CheckoutSessionId = Guid.NewGuid(),
                    IdempotencyKey = Guid.NewGuid().ToString("N"),
                    Currency = "PLN",
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                });
            }

            db.Rentals.Add(new Rental
            {
                Id = rentalId,
                TenantId = tenantId,
                CustomerId = customerId,
                MarketplaceOrderId = marketplaceOrderId,
                StartDateUtc = DateTime.UtcNow.AddDays(1),
                EndDateUtc = DateTime.UtcNow.AddDays(2),
                TotalAmount = 50,
                Status = RentalStatus.Pending
            });
            db.RentalItems.Add(new RentalItem
            {
                Id = Guid.NewGuid(),
                RentalId = rentalId,
                ProductId = productId,
                Quantity = 1,
                PricePerDay = 50,
                Subtotal = 50
            });
            await db.SaveChangesAsync();
        }

        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(x => x.GetCurrentTenantId())
            .Returns(ambientTenant ? tenantId : null);
        var sms = new Mock<ISmsSender>();
        var email = new Mock<IEmailSender>();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["App:BaseUrl"] = "https://app.example.test"
            })
            .Build();

        var service = new RentalConfirmationService(
            factory,
            tenantProvider.Object,
            Mock.Of<ILogger<RentalConfirmationService>>(),
            sms.Object,
            Mock.Of<IRentalNotificationService>(),
            config,
            email.Object);

        return new Fixture(tenantId, rentalId, factory, service, sms, email);
    }

    private sealed record Fixture(
        Guid TenantId,
        Guid RentalId,
        TestDbContextFactory Factory,
        RentalConfirmationService Service,
        Mock<ISmsSender> Sms,
        Mock<IEmailSender> Email);

    private sealed class TestDbContextFactory : IDbContextFactory<ApplicationDbContext>
    {
        private readonly DbContextOptions<ApplicationDbContext> _options;

        public TestDbContextFactory()
        {
            _options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString(), new InMemoryDatabaseRoot())
                .Options;
        }

        public ApplicationDbContext CreateDbContext() => new(_options);

        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
