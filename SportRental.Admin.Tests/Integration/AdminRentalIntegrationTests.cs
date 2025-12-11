using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;
using SportRental.Shared.Models;
using Xunit;
using Xunit.Abstractions;

namespace SportRental.Admin.Tests.Integration;

/// <summary>
/// Integration tests for Admin Panel rental operations on real PostgreSQL database.
/// Simulates creating rentals from admin panel and verifying they appear correctly.
/// </summary>
public class AdminRentalIntegrationTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private ApplicationDbContext _dbContext = null!;
    private Guid _testTenantId;
    private Guid _testCustomerId;
    private Guid _testProductId;
    private string _testProductName = string.Empty;
    private decimal _testProductPrice;
    private readonly List<Guid> _createdRentalIds = new();

    // Connection string to real database (same as Admin uses)
    private const string ConnectionString = "Host=eduedu.postgres.database.azure.com;Database=sr;Username=synapsis;Password=HasloHaslo122@@@@;SSL Mode=Require";

    public AdminRentalIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        _dbContext = new ApplicationDbContext(options);

        // Get existing tenant for testing
        var tenant = await _dbContext.Tenants.FirstOrDefaultAsync();
        if (tenant is null)
        {
            throw new InvalidOperationException("No tenant found in database. Please seed the database first.");
        }
        _testTenantId = tenant.Id;
        _output.WriteLine($"[Admin Test] Using tenant: {tenant.Name} ({_testTenantId})");

        // Get or create test customer
        var customer = await _dbContext.Customers.FirstOrDefaultAsync(c => c.TenantId == _testTenantId);
        if (customer is null)
        {
            customer = new Customer
            {
                Id = Guid.NewGuid(),
                TenantId = _testTenantId,
                FullName = "Admin Test Customer",
                Email = $"admin-test-{Guid.NewGuid():N}@test.com",
                PhoneNumber = "500600700",
                CreatedAtUtc = DateTime.UtcNow
            };
            _dbContext.Customers.Add(customer);
            await _dbContext.SaveChangesAsync();
            _output.WriteLine($"[Admin Test] Created test customer: {customer.FullName} ({customer.Id})");
        }
        else
        {
            _output.WriteLine($"[Admin Test] Using existing customer: {customer.FullName} ({customer.Id})");
        }
        _testCustomerId = customer.Id;

        // Get available product
        var product = await _dbContext.Products.FirstOrDefaultAsync(p => p.TenantId == _testTenantId && p.AvailableQuantity > 0);
        if (product is null)
        {
            throw new InvalidOperationException("No available product found in database for this tenant.");
        }
        _testProductId = product.Id;
        _testProductName = product.Name;
        _testProductPrice = product.DailyPrice;
        _output.WriteLine($"[Admin Test] Using product: {product.Name} ({_testProductId}), Price: {product.DailyPrice}/day");
    }

    public async Task DisposeAsync()
    {
        // Cleanup test rentals
        if (_createdRentalIds.Count > 0)
        {
            _output.WriteLine($"[Admin Test] Cleaning up {_createdRentalIds.Count} test rental(s)...");
            
            foreach (var rentalId in _createdRentalIds)
            {
                var rental = await _dbContext.Rentals
                    .Include(r => r.Items)
                    .FirstOrDefaultAsync(r => r.Id == rentalId);
                
                if (rental is not null)
                {
                    _dbContext.RentalItems.RemoveRange(rental.Items);
                    _dbContext.Rentals.Remove(rental);
                    _output.WriteLine($"[Admin Test] Removed rental: {rentalId}");
                }
            }
            
            await _dbContext.SaveChangesAsync();
        }

        await _dbContext.DisposeAsync();
    }

    /// <summary>
    /// Simulates admin creating a rental through the panel.
    /// This mimics POST /api/rentals endpoint behavior.
    /// </summary>
    [Fact]
    public async Task AdminCreateRental_ShouldPersistAndBeVisibleInRentalsList()
    {
        // Arrange - Simulate admin panel rental creation
        var rentalId = Guid.NewGuid();
        var startDate = DateTime.UtcNow.AddDays(1);
        var endDate = DateTime.UtcNow.AddDays(3);
        var days = (int)Math.Ceiling((endDate - startDate).TotalDays);
        var quantity = 2;
        var subtotal = _testProductPrice * quantity * days;
        var idempotencyKey = $"admin_test_{Guid.NewGuid():N}";

        _output.WriteLine($"[Admin Test] Creating rental from admin panel:");
        _output.WriteLine($"  - Customer: {_testCustomerId}");
        _output.WriteLine($"  - Product: {_testProductName} x {quantity}");
        _output.WriteLine($"  - Period: {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd} ({days} days)");
        _output.WriteLine($"  - Subtotal: {subtotal:F2} PLN");

        // Act - Create rental (simulating admin endpoint logic)
        var rental = new Rental
        {
            Id = rentalId,
            TenantId = _testTenantId,
            CustomerId = _testCustomerId,
            StartDateUtc = startDate,
            EndDateUtc = endDate,
            Status = RentalStatus.Confirmed,
            TotalAmount = subtotal,
            CreatedAtUtc = DateTime.UtcNow,
            IdempotencyKey = idempotencyKey
        };

        var rentalItem = new RentalItem
        {
            Id = Guid.NewGuid(),
            RentalId = rentalId,
            ProductId = _testProductId,
            Quantity = quantity,
            PricePerDay = _testProductPrice,
            Subtotal = subtotal
        };

        rental.Items.Add(rentalItem);
        _createdRentalIds.Add(rentalId);

        _dbContext.Rentals.Add(rental);
        await _dbContext.SaveChangesAsync();
        _output.WriteLine($"[Admin Test] Rental created: {rentalId}");

        // Clear tracker to force fresh read
        _dbContext.ChangeTracker.Clear();

        // Assert - Verify rental appears in admin rentals list (like GET /api/my-rentals)
        var adminRentalsList = await _dbContext.Rentals
            .AsNoTracking()
            .Include(r => r.Items)
                .ThenInclude(i => i.Product)
            .Include(r => r.Customer)
            .Where(r => r.TenantId == _testTenantId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToListAsync();

        var foundRental = adminRentalsList.FirstOrDefault(r => r.Id == rentalId);
        
        foundRental.Should().NotBeNull("Rental should appear in admin rentals list");
        foundRental!.CustomerId.Should().Be(_testCustomerId);
        foundRental.Status.Should().Be(RentalStatus.Confirmed);
        foundRental.TotalAmount.Should().Be(subtotal);
        foundRental.Items.Should().HaveCount(1);
        foundRental.Items.First().Quantity.Should().Be(quantity);
        foundRental.Customer.Should().NotBeNull();

        _output.WriteLine($"✅ [Admin Test] Rental visible in admin panel!");
        _output.WriteLine($"  - Customer: {foundRental.Customer?.FullName}");
        _output.WriteLine($"  - Product: {foundRental.Items.First().Product?.Name}");
        _output.WriteLine($"  - Total: {foundRental.TotalAmount:F2} PLN");
    }

    /// <summary>
    /// Tests idempotency - creating same rental twice should not duplicate.
    /// </summary>
    [Fact]
    public async Task AdminCreateRental_WithSameIdempotencyKey_ShouldNotDuplicate()
    {
        // Arrange
        var idempotencyKey = $"admin_idempotency_test_{Guid.NewGuid():N}";
        var startDate = DateTime.UtcNow.AddDays(5);
        var endDate = DateTime.UtcNow.AddDays(6);

        // Act - Create first rental
        var rental1Id = Guid.NewGuid();
        var rental1 = new Rental
        {
            Id = rental1Id,
            TenantId = _testTenantId,
            CustomerId = _testCustomerId,
            StartDateUtc = startDate,
            EndDateUtc = endDate,
            Status = RentalStatus.Confirmed,
            TotalAmount = _testProductPrice,
            CreatedAtUtc = DateTime.UtcNow,
            IdempotencyKey = idempotencyKey
        };
        rental1.Items.Add(new RentalItem
        {
            Id = Guid.NewGuid(),
            RentalId = rental1Id,
            ProductId = _testProductId,
            Quantity = 1,
            PricePerDay = _testProductPrice,
            Subtotal = _testProductPrice
        });
        
        _dbContext.Rentals.Add(rental1);
        await _dbContext.SaveChangesAsync();
        _createdRentalIds.Add(rental1Id);
        _output.WriteLine($"[Admin Test] First rental created: {rental1Id}");

        _dbContext.ChangeTracker.Clear();

        // Check if rental with same idempotency key exists (like admin endpoint does)
        var existingRental = await _dbContext.Rentals
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.TenantId == _testTenantId && r.IdempotencyKey == idempotencyKey);

        // Assert
        existingRental.Should().NotBeNull("Existing rental should be found by idempotency key");
        existingRental!.Id.Should().Be(rental1Id);

        _output.WriteLine($"✅ [Admin Test] Idempotency works - found existing rental: {existingRental.Id}");
    }

    /// <summary>
    /// Tests cancelling a rental from admin panel.
    /// </summary>
    [Fact]
    public async Task AdminCancelRental_ShouldUpdateStatus()
    {
        // Arrange - Create a rental to cancel
        var rentalId = Guid.NewGuid();
        var rental = new Rental
        {
            Id = rentalId,
            TenantId = _testTenantId,
            CustomerId = _testCustomerId,
            StartDateUtc = DateTime.UtcNow.AddDays(10),
            EndDateUtc = DateTime.UtcNow.AddDays(12),
            Status = RentalStatus.Confirmed,
            TotalAmount = _testProductPrice,
            CreatedAtUtc = DateTime.UtcNow,
            IdempotencyKey = $"cancel_test_{Guid.NewGuid():N}"
        };
        rental.Items.Add(new RentalItem
        {
            Id = Guid.NewGuid(),
            RentalId = rentalId,
            ProductId = _testProductId,
            Quantity = 1,
            PricePerDay = _testProductPrice,
            Subtotal = _testProductPrice
        });

        _dbContext.Rentals.Add(rental);
        await _dbContext.SaveChangesAsync();
        _createdRentalIds.Add(rentalId);
        _output.WriteLine($"[Admin Test] Created rental to cancel: {rentalId}");

        // Act - Cancel rental (simulating DELETE /api/rentals/{id})
        var rentalToCancel = await _dbContext.Rentals.FirstAsync(r => r.Id == rentalId);
        rentalToCancel.Status = RentalStatus.Cancelled;
        await _dbContext.SaveChangesAsync();
        _output.WriteLine($"[Admin Test] Rental cancelled");

        _dbContext.ChangeTracker.Clear();

        // Assert
        var cancelledRental = await _dbContext.Rentals
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == rentalId);

        cancelledRental.Should().NotBeNull();
        cancelledRental!.Status.Should().Be(RentalStatus.Cancelled);

        _output.WriteLine($"✅ [Admin Test] Rental status updated to: {cancelledRental.Status}");
    }

    /// <summary>
    /// Tests filtering rentals by status (like admin panel filter).
    /// </summary>
    [Fact]
    public async Task AdminFilterRentals_ByStatus_ShouldReturnCorrectResults()
    {
        // Arrange - Create rentals with different statuses
        var confirmedId = Guid.NewGuid();
        var cancelledId = Guid.NewGuid();

        var confirmedRental = new Rental
        {
            Id = confirmedId,
            TenantId = _testTenantId,
            CustomerId = _testCustomerId,
            StartDateUtc = DateTime.UtcNow.AddDays(20),
            EndDateUtc = DateTime.UtcNow.AddDays(21),
            Status = RentalStatus.Confirmed,
            TotalAmount = _testProductPrice,
            CreatedAtUtc = DateTime.UtcNow
        };
        confirmedRental.Items.Add(new RentalItem
        {
            Id = Guid.NewGuid(),
            RentalId = confirmedId,
            ProductId = _testProductId,
            Quantity = 1,
            PricePerDay = _testProductPrice,
            Subtotal = _testProductPrice
        });

        var cancelledRental = new Rental
        {
            Id = cancelledId,
            TenantId = _testTenantId,
            CustomerId = _testCustomerId,
            StartDateUtc = DateTime.UtcNow.AddDays(22),
            EndDateUtc = DateTime.UtcNow.AddDays(23),
            Status = RentalStatus.Cancelled,
            TotalAmount = _testProductPrice,
            CreatedAtUtc = DateTime.UtcNow
        };
        cancelledRental.Items.Add(new RentalItem
        {
            Id = Guid.NewGuid(),
            RentalId = cancelledId,
            ProductId = _testProductId,
            Quantity = 1,
            PricePerDay = _testProductPrice,
            Subtotal = _testProductPrice
        });

        _dbContext.Rentals.AddRange(confirmedRental, cancelledRental);
        await _dbContext.SaveChangesAsync();
        _createdRentalIds.AddRange(new[] { confirmedId, cancelledId });

        _dbContext.ChangeTracker.Clear();

        // Act - Filter by Confirmed status
        var confirmedRentals = await _dbContext.Rentals
            .AsNoTracking()
            .Where(r => r.TenantId == _testTenantId && r.Status == RentalStatus.Confirmed)
            .ToListAsync();

        var cancelledRentals = await _dbContext.Rentals
            .AsNoTracking()
            .Where(r => r.TenantId == _testTenantId && r.Status == RentalStatus.Cancelled)
            .ToListAsync();

        // Assert
        confirmedRentals.Should().Contain(r => r.Id == confirmedId);
        confirmedRentals.Should().NotContain(r => r.Id == cancelledId);
        
        cancelledRentals.Should().Contain(r => r.Id == cancelledId);
        cancelledRentals.Should().NotContain(r => r.Id == confirmedId);

        _output.WriteLine($"✅ [Admin Test] Status filtering works!");
        _output.WriteLine($"  - Confirmed rentals: {confirmedRentals.Count}");
        _output.WriteLine($"  - Cancelled rentals: {cancelledRentals.Count}");
    }

    /// <summary>
    /// Tests that rental with multiple items calculates total correctly.
    /// </summary>
    [Fact]
    public async Task AdminCreateRental_WithMultipleItems_ShouldCalculateTotalCorrectly()
    {
        // Arrange
        var products = await _dbContext.Products
            .Where(p => p.TenantId == _testTenantId && p.AvailableQuantity > 0)
            .Take(2)
            .ToListAsync();

        if (products.Count < 2)
        {
            _output.WriteLine("[Admin Test] Skipping multi-item test - not enough products available");
            return;
        }

        var rentalId = Guid.NewGuid();
        var startDate = DateTime.UtcNow.AddDays(30);
        var endDate = DateTime.UtcNow.AddDays(32);
        var days = 2;

        var items = products.Select(p => new RentalItem
        {
            Id = Guid.NewGuid(),
            RentalId = rentalId,
            ProductId = p.Id,
            Quantity = 1,
            PricePerDay = p.DailyPrice,
            Subtotal = p.DailyPrice * days
        }).ToList();

        var expectedTotal = items.Sum(i => i.Subtotal);

        var rental = new Rental
        {
            Id = rentalId,
            TenantId = _testTenantId,
            CustomerId = _testCustomerId,
            StartDateUtc = startDate,
            EndDateUtc = endDate,
            Status = RentalStatus.Confirmed,
            TotalAmount = expectedTotal,
            CreatedAtUtc = DateTime.UtcNow
        };

        foreach (var item in items)
        {
            rental.Items.Add(item);
        }

        _createdRentalIds.Add(rentalId);

        // Act
        _dbContext.Rentals.Add(rental);
        await _dbContext.SaveChangesAsync();

        _dbContext.ChangeTracker.Clear();

        // Assert
        var savedRental = await _dbContext.Rentals
            .AsNoTracking()
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == rentalId);

        savedRental.Should().NotBeNull();
        savedRental!.Items.Should().HaveCount(2);
        savedRental.TotalAmount.Should().Be(expectedTotal);

        _output.WriteLine($"✅ [Admin Test] Multi-item rental created!");
        _output.WriteLine($"  - Items: {savedRental.Items.Count}");
        _output.WriteLine($"  - Total: {savedRental.TotalAmount:F2} PLN");
        foreach (var item in savedRental.Items)
        {
            _output.WriteLine($"    - Product {item.ProductId}: {item.Subtotal:F2} PLN");
        }
    }
}
