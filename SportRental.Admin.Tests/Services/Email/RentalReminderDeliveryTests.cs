using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using SportRental.Admin.Services.Email;
using SportRental.Admin.Services.Sms;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;

namespace SportRental.Admin.Tests.Services.Email;

public class RentalReminderDeliveryTests
{
    [Fact]
    public async Task PrimaryReminder_UsesConfiguredEmailText()
    {
        await using var fixture = await ReminderFixture.CreateAsync(
            endDateUtc: DateTime.UtcNow.AddHours(12),
            configureCompany: company =>
                company.EmailReminderText = "Hej {imie}, zwrot {sprzet} do {data_konca}. {firma}");

        await fixture.TriggerAsync();

        fixture.Email.Verify(x => x.SendReminderAsync(
            "jan@example.test",
            "Jan Kowalski",
            It.Is<string>(text => text.Contains("Hej Jan Kowalski")
                && text.Contains("Rower testowy")
                && text.Contains("Test Rental"))), Times.Once);
    }

    [Fact]
    public async Task OverdueDay1_SendsConfiguredChannelsOnceAndRecordsEachDelivery()
    {
        await using var fixture = await ReminderFixture.CreateAsync(
            endDateUtc: DateTime.UtcNow.AddHours(-25),
            configureCompany: company =>
            {
                company.SmsReminderEnabled = true;
                company.EmailReminderText2 = "EMAIL2 {imie} {sprzet} {firma}";
                company.SmsReminderText2 = "SMS2 {imie} {sprzet} {firma}";
            });

        await fixture.TriggerAsync();
        await fixture.TriggerAsync();

        fixture.Email.Verify(x => x.SendReminderAsync(
            "jan@example.test",
            "Jan Kowalski",
            It.Is<string>(text => text.StartsWith("EMAIL2"))), Times.Once);
        fixture.Sms.Verify(x => x.SendReminderAsync(
            "+48123456789",
            "Jan Kowalski",
            It.Is<string>(text => text.StartsWith("SMS2")),
            It.IsAny<CancellationToken>()), Times.Once);

        await using var db = fixture.Factory.CreateDbContext();
        var deliveries = await db.RentalReminderDeliveries
            .Where(d => d.RentalId == fixture.RentalId
                && d.Stage == RentalReminderStage.OverdueDay1)
            .ToListAsync();
        deliveries.Should().HaveCount(2);
        deliveries.Select(d => d.Channel).Should().BeEquivalentTo(
            new[] { RentalReminderChannel.Email, RentalReminderChannel.Sms });
    }

    [Fact]
    public async Task FinalReminder_RetriesOnlyFailedChannelBeforeMarkingStageComplete()
    {
        await using var fixture = await ReminderFixture.CreateAsync(
            endDateUtc: DateTime.UtcNow.AddMinutes(20),
            configureCompany: company => company.SmsReminderEnabled = true);
        fixture.Sms
            .Setup(x => x.SendReminderAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SMS unavailable"));

        await fixture.TriggerAsync();

        await using (var db = fixture.Factory.CreateDbContext())
        {
            var rental = await db.Rentals.SingleAsync(r => r.Id == fixture.RentalId);
            rental.IsFinalReminderSent.Should().BeFalse();
            (await db.RentalReminderDeliveries.CountAsync(d =>
                d.RentalId == fixture.RentalId
                && d.Stage == RentalReminderStage.Final
                && d.Channel == RentalReminderChannel.Email)).Should().Be(1);
            (await db.RentalReminderDeliveries.CountAsync(d =>
                d.RentalId == fixture.RentalId
                && d.Stage == RentalReminderStage.Final
                && d.Channel == RentalReminderChannel.Sms)).Should().Be(0);
        }

        fixture.Sms
            .Setup(x => x.SendReminderAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await fixture.TriggerAsync();

        await using (var db = fixture.Factory.CreateDbContext())
        {
            var rental = await db.Rentals.SingleAsync(r => r.Id == fixture.RentalId);
            rental.IsFinalReminderSent.Should().BeTrue();
            (await db.RentalReminderDeliveries.CountAsync(d =>
                d.RentalId == fixture.RentalId
                && d.Stage == RentalReminderStage.Final)).Should().Be(2);
        }

        fixture.Email.Verify(x => x.SendReminderAsync(
            It.IsAny<string>(), It.IsAny<string>(),
            It.Is<string>(text => text.Contains("Ostatnie przypomnienie"))), Times.Once);
    }

    [Fact]
    public async Task FinalWindow_DoesNotSendMissedPrimaryReminderAlongsideFinal()
    {
        await using var fixture = await ReminderFixture.CreateAsync(
            endDateUtc: DateTime.UtcNow.AddMinutes(20),
            configureCompany: company => company.SmsReminderEnabled = true);

        await fixture.TriggerAsync();

        fixture.Email.Verify(x => x.SendReminderAsync(
            "jan@example.test",
            "Jan Kowalski",
            It.Is<string>(text => text.Contains("Ostatnie przypomnienie"))), Times.Once);
        fixture.Email.Verify(x => x.SendReminderAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        fixture.Sms.Verify(x => x.SendReminderAsync(
            "+48123456789",
            "Jan Kowalski",
            It.Is<string>(text => text.Contains("zostalo ok.")),
            It.IsAny<CancellationToken>()), Times.Once);

        await using var db = fixture.Factory.CreateDbContext();
        var rental = await db.Rentals.SingleAsync(r => r.Id == fixture.RentalId);
        rental.IsReminderEmailSent.Should().BeFalse();
        rental.IsReminderSmsSent.Should().BeFalse();
        rental.IsFinalReminderSent.Should().BeTrue();
    }

    private sealed class ReminderFixture : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly RentalReminderService _service;

        private ReminderFixture(
            Guid rentalId,
            TestDbContextFactory factory,
            Mock<IEmailSender> email,
            Mock<ISmsSender> sms,
            ServiceProvider provider,
            RentalReminderService service)
        {
            RentalId = rentalId;
            Factory = factory;
            Email = email;
            Sms = sms;
            _provider = provider;
            _service = service;
        }

        public Guid RentalId { get; }
        public TestDbContextFactory Factory { get; }
        public Mock<IEmailSender> Email { get; }
        public Mock<ISmsSender> Sms { get; }

        public static async Task<ReminderFixture> CreateAsync(
            DateTime endDateUtc,
            Action<CompanyInfo>? configureCompany = null)
        {
            var tenantId = Guid.NewGuid();
            var customerId = Guid.NewGuid();
            var productId = Guid.NewGuid();
            var rentalId = Guid.NewGuid();
            var factory = new TestDbContextFactory();
            var company = new CompanyInfo
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Name = "Test Rental",
                PhoneNumber = "+48999888777"
            };
            configureCompany?.Invoke(company);

            await using (var db = factory.CreateDbContext())
            {
                db.Tenants.Add(new Tenant { Id = tenantId, Name = "Test Rental" });
                db.CompanyInfos.Add(company);
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
                db.Rentals.Add(new Rental
                {
                    Id = rentalId,
                    TenantId = tenantId,
                    CustomerId = customerId,
                    StartDateUtc = endDateUtc.AddDays(-2),
                    EndDateUtc = endDateUtc,
                    Status = RentalStatus.Active,
                    RentalType = RentalType.Daily,
                    IssuedAtUtc = endDateUtc.AddDays(-2)
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

            var email = new Mock<IEmailSender>();
            email.Setup(x => x.SendReminderAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(Task.CompletedTask);
            var sms = new Mock<ISmsSender>();
            sms.Setup(x => x.SendReminderAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var services = new ServiceCollection()
                .AddSingleton<IDbContextFactory<ApplicationDbContext>>(factory)
                .AddSingleton(email.Object)
                .AddSingleton(sms.Object)
                .BuildServiceProvider();
            var service = new RentalReminderService(
                services.GetRequiredService<IServiceScopeFactory>(),
                Mock.Of<ILogger<RentalReminderService>>());

            return new ReminderFixture(rentalId, factory, email, sms, services, service);
        }

        public async Task TriggerAsync()
        {
            var method = typeof(RentalReminderService)
                .GetMethod("CheckRentalsForReminders", BindingFlags.Instance | BindingFlags.NonPublic);
            var task = method!.Invoke(_service, new object?[] { null }) as Task;
            task.Should().NotBeNull();
            await task!;
        }

        public async ValueTask DisposeAsync()
        {
            _service.Dispose();
            await _provider.DisposeAsync();
        }
    }

    public sealed class TestDbContextFactory : IDbContextFactory<ApplicationDbContext>
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
