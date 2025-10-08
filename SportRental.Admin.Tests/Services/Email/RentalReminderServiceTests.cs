using System.Reflection;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;
using SportRental.Admin.Services.Email;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace SportRental.Admin.Tests.Services.Email;

public class RentalReminderServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly Mock<IEmailSender> _emailSenderMock;
    private readonly Mock<ILogger<RentalReminderService>> _loggerMock;
    private readonly Mock<IServiceScope> _serviceScopeMock;
    private readonly Mock<IServiceScopeFactory> _serviceScopeFactoryMock;
    private readonly RentalReminderService _reminderService;
    private readonly Guid _testTenantId = Guid.NewGuid();

    public RentalReminderServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new ApplicationDbContext(options);

        _emailSenderMock = new Mock<IEmailSender>();
        _loggerMock = new Mock<ILogger<RentalReminderService>>();
        _serviceScopeMock = new Mock<IServiceScope>();
        _serviceScopeFactoryMock = new Mock<IServiceScopeFactory>();

        _serviceScopeFactoryMock.Setup(x => x.CreateScope()).Returns(_serviceScopeMock.Object);

        var scopedServiceProviderMock = new Mock<IServiceProvider>();
        scopedServiceProviderMock
            .Setup(x => x.GetService(typeof(IDbContextFactory<ApplicationDbContext>)))
            .Returns(new TestDbContextFactory(_dbContext));
        scopedServiceProviderMock
            .Setup(x => x.GetService(typeof(IEmailSender)))
            .Returns(_emailSenderMock.Object);

        _serviceScopeMock.Setup(x => x.ServiceProvider).Returns(scopedServiceProviderMock.Object);

        _reminderService = new RentalReminderService(_serviceScopeFactoryMock.Object, _loggerMock.Object);

        SeedTestData();
    }

    private void SeedTestData()
    {
        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            TenantId = _testTenantId,
            FullName = "Jan Kowalski",
            Email = "jan.kowalski@test.com",
            PhoneNumber = "+48123456789"
        };

        var product = new Product
        {
            Id = Guid.NewGuid(),
            TenantId = _testTenantId,
            Name = "Rower górski",
            Sku = "BIKE001",
            DailyPrice = 50.00m,
            AvailableQuantity = 5
        };

        var rentalSoon = new Rental
        {
            Id = Guid.NewGuid(),
            TenantId = _testTenantId,
            CustomerId = customer.Id,
            StartDateUtc = DateTime.UtcNow.AddDays(-2),
            EndDateUtc = DateTime.UtcNow.AddHours(12),
            Status = RentalStatus.Active,
            TotalAmount = 100.00m,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-2)
        };

        var rentalLater = new Rental
        {
            Id = Guid.NewGuid(),
            TenantId = _testTenantId,
            CustomerId = customer.Id,
            StartDateUtc = DateTime.UtcNow.AddDays(-1),
            EndDateUtc = DateTime.UtcNow.AddDays(2),
            Status = RentalStatus.Active,
            TotalAmount = 150.00m,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1)
        };

        var rentalCompleted = new Rental
        {
            Id = Guid.NewGuid(),
            TenantId = _testTenantId,
            CustomerId = customer.Id,
            StartDateUtc = DateTime.UtcNow.AddDays(-3),
            EndDateUtc = DateTime.UtcNow.AddHours(6),
            Status = RentalStatus.Completed,
            TotalAmount = 75.00m,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-3)
        };

        _dbContext.Customers.Add(customer);
        _dbContext.Products.Add(product);
        _dbContext.Rentals.AddRange(rentalSoon, rentalLater, rentalCompleted);
        _dbContext.SaveChanges();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldSendRemindersForRentalsEndingWithin24Hours()
    {
        await TriggerReminderAsync();

        _emailSenderMock.Verify(
            x => x.SendReminderAsync(
                "jan.kowalski@test.com",
                "Jan Kowalski",
                It.Is<string>(msg => msg.Contains("przypominamy", StringComparison.OrdinalIgnoreCase))),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_WithNoCustomerEmail_ShouldNotSendEmail()
    {
        var customer = _dbContext.Customers.First();
        customer.Email = null;
        _dbContext.SaveChanges();

        await TriggerReminderAsync();

        _emailSenderMock.Verify(
            x => x.SendReminderAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WithEmailSenderException_ShouldContinueProcessing()
    {
        _emailSenderMock
            .Setup(x => x.SendReminderAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("Email service unavailable"));

        Func<Task> act = () => TriggerReminderAsync();
        await act.Should().NotThrowAsync();

        _emailSenderMock.Verify(
            x => x.SendReminderAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.AtLeastOnce);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private async Task TriggerReminderAsync()
    {
        var method = typeof(RentalReminderService)
            .GetMethod("CheckRentalsForReminders", BindingFlags.Instance | BindingFlags.NonPublic);
        method!.Invoke(_reminderService, new object?[] { null });
        await Task.Delay(50);
    }

    private class TestDbContextFactory : IDbContextFactory<ApplicationDbContext>
    {
        private readonly ApplicationDbContext _context;

        public TestDbContextFactory(ApplicationDbContext context)
        {
            _context = context;
        }

        public ApplicationDbContext CreateDbContext() => _context;

        public Task<ApplicationDbContext> CreateDbContextAsync()
            => Task.FromResult(_context);
    }
}
