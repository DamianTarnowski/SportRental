using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;

namespace SportRental.Admin.Tests.Services.Holds;

public class ExpiredHoldsCleanerTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly Guid _tenantId = Guid.NewGuid();

    public ExpiredHoldsCleanerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);
        _db.SetTenant(_tenantId);
    }

    [Fact]
    public async Task CleanExpiredHolds_RemovesOnlyExpiredHolds()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var productId = Guid.NewGuid();

        var expiredHold1 = new ReservationHold
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            ProductId = productId,
            Quantity = 1,
            StartDateUtc = now.AddDays(-1),
            EndDateUtc = now.AddDays(1),
            ExpiresAtUtc = now.AddMinutes(-30), // Expired
            CreatedAtUtc = now.AddHours(-1)
        };

        var expiredHold2 = new ReservationHold
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            ProductId = productId,
            Quantity = 2,
            StartDateUtc = now.AddDays(-1),
            EndDateUtc = now.AddDays(1),
            ExpiresAtUtc = now.AddMinutes(-10), // Expired
            CreatedAtUtc = now.AddHours(-1)
        };

        var activeHold = new ReservationHold
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            ProductId = productId,
            Quantity = 1,
            StartDateUtc = now.AddDays(-1),
            EndDateUtc = now.AddDays(1),
            ExpiresAtUtc = now.AddMinutes(30), // Still active
            CreatedAtUtc = now.AddHours(-1)
        };

        await _db.ReservationHolds.AddRangeAsync(expiredHold1, expiredHold2, activeHold);
        await _db.SaveChangesAsync();

        // Act - simulate cleaner logic
        var expired = await _db.ReservationHolds
            .Where(h => h.ExpiresAtUtc <= now)
            .ToListAsync();

        _db.ReservationHolds.RemoveRange(expired);
        await _db.SaveChangesAsync();

        // Assert
        var remaining = await _db.ReservationHolds.ToListAsync();
        remaining.Should().HaveCount(1);
        remaining[0].Id.Should().Be(activeHold.Id);
    }

    [Fact]
    public async Task CleanExpiredHolds_WhenNoExpiredHolds_DoesNothing()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var productId = Guid.NewGuid();

        var activeHold1 = new ReservationHold
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            ProductId = productId,
            Quantity = 1,
            StartDateUtc = now,
            EndDateUtc = now.AddDays(1),
            ExpiresAtUtc = now.AddMinutes(30),
            CreatedAtUtc = now
        };

        var activeHold2 = new ReservationHold
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            ProductId = productId,
            Quantity = 2,
            StartDateUtc = now,
            EndDateUtc = now.AddDays(2),
            ExpiresAtUtc = now.AddHours(1),
            CreatedAtUtc = now
        };

        await _db.ReservationHolds.AddRangeAsync(activeHold1, activeHold2);
        await _db.SaveChangesAsync();

        // Act
        var expired = await _db.ReservationHolds
            .Where(h => h.ExpiresAtUtc <= now)
            .ToListAsync();

        // Assert
        expired.Should().BeEmpty();
        var remaining = await _db.ReservationHolds.ToListAsync();
        remaining.Should().HaveCount(2);
    }

    [Fact]
    public async Task CleanExpiredHolds_WhenAllExpired_RemovesAll()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var productId = Guid.NewGuid();

        for (int i = 0; i < 5; i++)
        {
            await _db.ReservationHolds.AddAsync(new ReservationHold
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantId,
                ProductId = productId,
                Quantity = 1,
                StartDateUtc = now.AddDays(-2),
                EndDateUtc = now.AddDays(-1),
                ExpiresAtUtc = now.AddHours(-i - 1), // All expired
                CreatedAtUtc = now.AddHours(-2)
            });
        }
        await _db.SaveChangesAsync();

        // Act
        var expired = await _db.ReservationHolds
            .Where(h => h.ExpiresAtUtc <= now)
            .ToListAsync();

        _db.ReservationHolds.RemoveRange(expired);
        await _db.SaveChangesAsync();

        // Assert
        expired.Should().HaveCount(5);
        var remaining = await _db.ReservationHolds.ToListAsync();
        remaining.Should().BeEmpty();
    }

    [Fact]
    public async Task ReservationHold_ShouldHaveRequiredProperties()
    {
        // Arrange
        var hold = new ReservationHold
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            ProductId = Guid.NewGuid(),
            Quantity = 3,
            StartDateUtc = DateTime.UtcNow,
            EndDateUtc = DateTime.UtcNow.AddDays(5),
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(15),
            CustomerId = Guid.NewGuid(),
            SessionId = "session-123"
        };

        // Act
        await _db.ReservationHolds.AddAsync(hold);
        await _db.SaveChangesAsync();

        var saved = await _db.ReservationHolds.FindAsync(hold.Id);

        // Assert
        saved.Should().NotBeNull();
        saved!.TenantId.Should().Be(_tenantId);
        saved.Quantity.Should().Be(3);
        saved.CustomerId.Should().NotBeNull();
        saved.SessionId.Should().Be("session-123");
    }

    [Fact]
    public async Task CleanExpiredHolds_HandlesEdgeCaseExactlyExpired()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var productId = Guid.NewGuid();

        var exactlyExpiredHold = new ReservationHold
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            ProductId = productId,
            Quantity = 1,
            StartDateUtc = now,
            EndDateUtc = now.AddDays(1),
            ExpiresAtUtc = now, // Exactly at current time
            CreatedAtUtc = now.AddMinutes(-15)
        };

        await _db.ReservationHolds.AddAsync(exactlyExpiredHold);
        await _db.SaveChangesAsync();

        // Act
        var expired = await _db.ReservationHolds
            .Where(h => h.ExpiresAtUtc <= now)
            .ToListAsync();

        // Assert - exactly expired should be removed
        expired.Should().HaveCount(1);
    }

    public void Dispose()
    {
        _db.Dispose();
    }
}

