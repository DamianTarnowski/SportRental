using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;
using SportRental.Infrastructure.Tenancy;
using SportRental.Admin.Services.Sms;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;

namespace SportRental.Admin.Tests.Services;

public class SmsConfirmationServiceTests : IDisposable
{
    private readonly DbContextOptions<ApplicationDbContext> _options;
    private readonly Mock<IDbContextFactory<ApplicationDbContext>> _contextFactoryMock;
    private readonly Mock<ITenantProvider> _tenantProviderMock;
    private readonly Mock<ILogger<SmsConfirmationService>> _loggerMock;
    private readonly SmsConfirmationService _smsConfirmationService;

    public SmsConfirmationServiceTests()
    {
        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"test_sms_{Guid.NewGuid()}")
            .ConfigureWarnings(b => b.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _contextFactoryMock = new Mock<IDbContextFactory<ApplicationDbContext>>();
        _contextFactoryMock.Setup(x => x.CreateDbContext()).Returns(() => CreateContext());
        _contextFactoryMock.Setup(x => x.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => CreateContext());

        _tenantProviderMock = new Mock<ITenantProvider>();
        _loggerMock = new Mock<ILogger<SmsConfirmationService>>();

        using var init = CreateContext();
        init.Database.EnsureCreated();

        _smsConfirmationService = new SmsConfirmationService(
            _contextFactoryMock.Object,
            _tenantProviderMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task GenerateConfirmationCodeAsync_WithValidRental_ShouldCreateConfirmation()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var rentalId = Guid.NewGuid();
        _tenantProviderMock.Setup(x => x.GetCurrentTenantId()).Returns(tenantId);

        await using (var context = CreateContext())
        {
            context.Customers.Add(new Customer
            {
                Id = customerId,
                TenantId = tenantId,
                FullName = "Jan Kowalski",
                PhoneNumber = "+48123456789"
            });

            context.Rentals.Add(new Rental
            {
                Id = rentalId,
                TenantId = tenantId,
                CustomerId = customerId,                StartDateUtc = DateTime.UtcNow.Date,
                EndDateUtc = DateTime.UtcNow.Date.AddDays(3),
                Status = RentalStatus.Pending
            });

            await context.SaveChangesAsync();
        }

        var code = await _smsConfirmationService.GenerateConfirmationCodeAsync(rentalId);

        code.Should().NotBeNullOrEmpty();
        code.Should().HaveLength(6);

        await using var assertContext = CreateContext();
        var confirmation = await assertContext.SmsConfirmations.SingleAsync();
        confirmation.RentalId.Should().Be(rentalId);
        confirmation.Code.Should().Be(code);
        confirmation.PhoneNumber.Should().Be("+48123456789");
        confirmation.TenantId.Should().Be(tenantId);
        confirmation.IsConfirmed.Should().BeFalse();
    }

    [Fact]
    public async Task GenerateConfirmationCodeAsync_WithoutTenant_ShouldThrowException()
    {
        _tenantProviderMock.Setup(x => x.GetCurrentTenantId()).Returns((Guid?)null);

        Func<Task> act = () => _smsConfirmationService.GenerateConfirmationCodeAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("No tenant context available");
    }

    [Fact]
    public async Task GenerateConfirmationCodeAsync_WithNonexistentRental_ShouldThrowException()
    {
        _tenantProviderMock.Setup(x => x.GetCurrentTenantId()).Returns(Guid.NewGuid());

        Func<Task> act = () => _smsConfirmationService.GenerateConfirmationCodeAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Rental or customer phone number not found");
    }

    [Fact]
    public async Task GenerateConfirmationCodeAsync_WithExistingConfirmation_ShouldReplaceOld()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var rentalId = Guid.NewGuid();
        _tenantProviderMock.Setup(x => x.GetCurrentTenantId()).Returns(tenantId);

        var existingConfirmation = new SmsConfirmation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            RentalId = rentalId,
            Code = "123456",
            PhoneNumber = "+48987654321",
            CreatedAt = DateTime.UtcNow.AddMinutes(-30),
            ExpiresAt = DateTime.UtcNow.AddHours(23.5)
        };

        await using (var context = CreateContext())
        {
            context.Customers.Add(new Customer
            {
                Id = customerId,
                TenantId = tenantId,
                FullName = "Anna Nowak",
                PhoneNumber = "+48987654321"
            });

            context.Rentals.Add(new Rental
            {
                Id = rentalId,
                TenantId = tenantId,
                CustomerId = customerId,                StartDateUtc = DateTime.UtcNow.Date,
                EndDateUtc = DateTime.UtcNow.Date.AddDays(2),
                Status = RentalStatus.Pending
            });

            context.SmsConfirmations.Add(existingConfirmation);
            await context.SaveChangesAsync();
        }

        var newCode = await _smsConfirmationService.GenerateConfirmationCodeAsync(rentalId);

        await using var assertContext = CreateContext();
        var confirmations = await assertContext.SmsConfirmations.ToListAsync();
        confirmations.Should().HaveCount(1);
        confirmations[0].Code.Should().Be(newCode);
        confirmations[0].Id.Should().NotBe(existingConfirmation.Id);
    }

    [Fact]
    public async Task ValidateConfirmationCodeAsync_WithValidCode_ShouldReturnTrueAndMarkConfirmed()
    {
        var tenantId = Guid.NewGuid();
        var rentalId = Guid.NewGuid();
        var code = "654321";
        _tenantProviderMock.Setup(x => x.GetCurrentTenantId()).Returns(tenantId);

        await using (var context = CreateContext())
        {
            context.SmsConfirmations.Add(new SmsConfirmation
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                RentalId = rentalId,
                Code = code,
                PhoneNumber = "+48111222333",
                IsConfirmed = false,
                CreatedAt = DateTime.UtcNow.AddMinutes(-5),
                ExpiresAt = DateTime.UtcNow.AddHours(23),
                AttemptsCount = 0
            });

            await context.SaveChangesAsync();
        }

        var result = await _smsConfirmationService.ValidateConfirmationCodeAsync(rentalId, code);

        result.Should().BeTrue();

        await using var assertContext = CreateContext();
        var confirmationEntity = await assertContext.SmsConfirmations.SingleAsync();
        confirmationEntity.IsConfirmed.Should().BeTrue();
        confirmationEntity.AttemptsCount.Should().Be(1);
        confirmationEntity.ConfirmedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ValidateConfirmationCodeAsync_WithInvalidCode_ShouldReturnFalse()
    {
        var tenantId = Guid.NewGuid();
        var rentalId = Guid.NewGuid();
        _tenantProviderMock.Setup(x => x.GetCurrentTenantId()).Returns(tenantId);

        await using (var context = CreateContext())
        {
            context.SmsConfirmations.Add(new SmsConfirmation
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                RentalId = rentalId,
                Code = "999999",
                PhoneNumber = "+48444555666",
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            });
            await context.SaveChangesAsync();
        }

        var result = await _smsConfirmationService.ValidateConfirmationCodeAsync(rentalId, "111111");

        result.Should().BeFalse();

        await using var assertContext = CreateContext();
        var confirmation = await assertContext.SmsConfirmations.SingleAsync();
        confirmation.AttemptsCount.Should().Be(0);
        confirmation.IsConfirmed.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateConfirmationCodeAsync_WithExpiredCode_ShouldReturnFalse()
    {
        var tenantId = Guid.NewGuid();
        var rentalId = Guid.NewGuid();
        _tenantProviderMock.Setup(x => x.GetCurrentTenantId()).Returns(tenantId);

        await using (var context = CreateContext())
        {
            context.SmsConfirmations.Add(new SmsConfirmation
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                RentalId = rentalId,
                Code = "999999",
                PhoneNumber = "+48777888999",
                CreatedAt = DateTime.UtcNow.AddHours(-2),
                ExpiresAt = DateTime.UtcNow.AddMinutes(-10)
            });
            await context.SaveChangesAsync();
        }

        var result = await _smsConfirmationService.ValidateConfirmationCodeAsync(rentalId, "999999");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateConfirmationCodeAsync_WithTooManyAttempts_ShouldReturnFalse()
    {
        var tenantId = Guid.NewGuid();
        var rentalId = Guid.NewGuid();
        _tenantProviderMock.Setup(x => x.GetCurrentTenantId()).Returns(tenantId);

        await using (var context = CreateContext())
        {
            context.SmsConfirmations.Add(new SmsConfirmation
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                RentalId = rentalId,
                Code = "111222",
                PhoneNumber = "+48700112233",
                AttemptsCount = 4,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            });
            await context.SaveChangesAsync();
        }

        var result = await _smsConfirmationService.ValidateConfirmationCodeAsync(rentalId, "111222");

        result.Should().BeFalse();

        await using var assertContext = CreateContext();
        var confirmation = await assertContext.SmsConfirmations.SingleAsync();
        confirmation.AttemptsCount.Should().Be(5);
        confirmation.IsConfirmed.Should().BeFalse();
    }

    [Fact]
    public async Task MarkRentalAsConfirmedAsync_WithValidRental_ShouldUpdateRental()
    {
        var tenantId = Guid.NewGuid();
        var rentalId = Guid.NewGuid();
        _tenantProviderMock.Setup(x => x.GetCurrentTenantId()).Returns(tenantId);

        await using (var context = CreateContext())
        {
            context.Rentals.Add(new Rental
            {
                Id = rentalId,
                TenantId = tenantId,
                CustomerId = Guid.NewGuid(),
                Status = RentalStatus.Pending,
                StartDateUtc = DateTime.UtcNow.Date,
                EndDateUtc = DateTime.UtcNow.Date.AddDays(1)
            });

            context.SmsConfirmations.Add(new SmsConfirmation
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                RentalId = rentalId,
                Code = "222333",
                PhoneNumber = "+48444455555",
                IsConfirmed = true,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            });

            await context.SaveChangesAsync();
        }

        await _smsConfirmationService.MarkRentalAsConfirmedAsync(rentalId);

        await using var assertContext = CreateContext();
        var rental = await assertContext.Rentals.SingleAsync();
        rental.Status.Should().Be(RentalStatus.Confirmed);
        rental.IsSmsConfirmed.Should().BeTrue();
    }

    [Fact]
    public async Task MarkRentalAsConfirmedAsync_WithNonPendingRental_ShouldOnlyUpdateSmsConfirmed()
    {
        var tenantId = Guid.NewGuid();
        var rentalId = Guid.NewGuid();
        _tenantProviderMock.Setup(x => x.GetCurrentTenantId()).Returns(tenantId);

        await using (var context = CreateContext())
        {
            context.Rentals.Add(new Rental
            {
                Id = rentalId,
                TenantId = tenantId,
                CustomerId = Guid.NewGuid(),
                Status = RentalStatus.Completed,
                StartDateUtc = DateTime.UtcNow.Date,
                EndDateUtc = DateTime.UtcNow.Date.AddDays(1)
            });

            context.SmsConfirmations.Add(new SmsConfirmation
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                RentalId = rentalId,
                Code = "333444",
                PhoneNumber = "+48333344444",
                IsConfirmed = false,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            });

            await context.SaveChangesAsync();
        }

        await _smsConfirmationService.MarkRentalAsConfirmedAsync(rentalId);

        await using var assertContext = CreateContext();
        var rental = await assertContext.Rentals.SingleAsync();
        rental.Status.Should().Be(RentalStatus.Completed);
        rental.IsSmsConfirmed.Should().BeTrue();

        var confirmation = await assertContext.SmsConfirmations.SingleAsync();
        confirmation.IsConfirmed.Should().BeFalse();
    }

    public void Dispose()
    {
        using var context = CreateContext();
        context.Database.EnsureDeleted();
    }

    private ApplicationDbContext CreateContext() => new ApplicationDbContext(_options);
}




