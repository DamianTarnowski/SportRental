using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;
using SportRental.Shared.Models;
using Xunit;
using Xunit.Abstractions;

namespace SportRental.Api.Tests.Integration;

/// <summary>
/// Integration tests for Rental operations on real PostgreSQL database.
/// These tests verify that rentals are correctly created and visible in the database.
/// </summary>
public class RentalIntegrationTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private ApplicationDbContext _dbContext = null!;
    private Guid _testTenantId;
    private Guid _testCustomerId;
    private Guid _testProductId;
    private readonly List<Guid> _createdRentalIds = new();

    // Connection string to real database
    private const string ConnectionString = "Host=eduedu.postgres.database.azure.com;Database=sr;Username=synapsis;Password=HasloHaslo122@@@@;SSL Mode=Require";

    public RentalIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        _dbContext = new ApplicationDbContext(options);

        // Get existing tenant, customer and product for testing
        var tenant = await _dbContext.Tenants.FirstOrDefaultAsync();
        if (tenant is null)
        {
            throw new InvalidOperationException("No tenant found in database. Please seed the database first.");
        }
        _testTenantId = tenant.Id;
        _output.WriteLine($"Using tenant: {tenant.Name} ({_testTenantId})");

        var customer = await _dbContext.Customers.FirstOrDefaultAsync(c => c.TenantId == _testTenantId);
        if (customer is null)
        {
            // Create test customer
            customer = new Customer
            {
                Id = Guid.NewGuid(),
                TenantId = _testTenantId,
                FullName = "Test Customer Integration",
                Email = $"test-integration-{Guid.NewGuid():N}@test.com",
                PhoneNumber = "123456789",
                CreatedAtUtc = DateTime.UtcNow
            };
            _dbContext.Customers.Add(customer);
            await _dbContext.SaveChangesAsync();
            _output.WriteLine($"Created test customer: {customer.FullName} ({customer.Id})");
        }
        else
        {
            _output.WriteLine($"Using existing customer: {customer.FullName} ({customer.Id})");
        }
        _testCustomerId = customer.Id;

        var product = await _dbContext.Products.FirstOrDefaultAsync(p => p.TenantId == _testTenantId && p.AvailableQuantity > 0);
        if (product is null)
        {
            throw new InvalidOperationException("No available product found in database for this tenant.");
        }
        _testProductId = product.Id;
        _output.WriteLine($"Using product: {product.Name} ({_testProductId}), Price: {product.DailyPrice}/day");
    }

    public async Task DisposeAsync()
    {
        // Cleanup: Remove test rentals created during tests
        if (_createdRentalIds.Count > 0)
        {
            _output.WriteLine($"Cleaning up {_createdRentalIds.Count} test rental(s)...");
            
            foreach (var rentalId in _createdRentalIds)
            {
                var rental = await _dbContext.Rentals
                    .Include(r => r.Items)
                    .FirstOrDefaultAsync(r => r.Id == rentalId);
                
                if (rental is not null)
                {
                    _dbContext.RentalItems.RemoveRange(rental.Items);
                    _dbContext.Rentals.Remove(rental);
                    _output.WriteLine($"Removed rental: {rentalId}");
                }
            }
            
            await _dbContext.SaveChangesAsync();
        }

        await _dbContext.DisposeAsync();
    }

    [Fact]
    public async Task CreateRental_ShouldPersistToDatabase_AndBeRetrievable()
    {
        // Arrange
        var rentalId = Guid.NewGuid();
        var startDate = DateTime.UtcNow.AddDays(1);
        var endDate = DateTime.UtcNow.AddDays(3);
        var rentalDays = (int)Math.Ceiling((endDate - startDate).TotalDays);
        
        var product = await _dbContext.Products.FindAsync(_testProductId);
        var pricePerDay = product!.DailyPrice;
        var quantity = 1;
        var totalAmount = pricePerDay * quantity * rentalDays;
        var depositAmount = totalAmount * 0.3m; // 30% deposit

        var rental = new Rental
        {
            Id = rentalId,
            TenantId = _testTenantId,
            CustomerId = _testCustomerId,
            StartDateUtc = startDate,
            EndDateUtc = endDate,
            Status = RentalStatus.Confirmed,
            TotalAmount = totalAmount,
            DepositAmount = depositAmount,
            PaymentIntentId = $"pi_test_{Guid.NewGuid():N}",
            PaymentStatus = PaymentIntentStatus.Succeeded,
            IdempotencyKey = $"test_{Guid.NewGuid():N}",
            CreatedAtUtc = DateTime.UtcNow,
            Notes = "Integration test rental"
        };

        var rentalItem = new RentalItem
        {
            Id = Guid.NewGuid(),
            RentalId = rentalId,
            ProductId = _testProductId,
            Quantity = quantity,
            PricePerDay = pricePerDay,
            Subtotal = pricePerDay * quantity * rentalDays
        };

        rental.Items.Add(rentalItem);
        _createdRentalIds.Add(rentalId);

        _output.WriteLine($"Creating rental: {rentalId}");
        _output.WriteLine($"  - Start: {startDate:yyyy-MM-dd HH:mm}");
        _output.WriteLine($"  - End: {endDate:yyyy-MM-dd HH:mm}");
        _output.WriteLine($"  - Days: {rentalDays}");
        _output.WriteLine($"  - Total: {totalAmount:F2} PLN");
        _output.WriteLine($"  - Deposit: {depositAmount:F2} PLN");

        // Act - Create rental
        _dbContext.Rentals.Add(rental);
        await _dbContext.SaveChangesAsync();
        _output.WriteLine("Rental saved to database.");

        // Detach to force fresh read from database
        _dbContext.ChangeTracker.Clear();

        // Assert - Verify rental exists in database
        var retrievedRental = await _dbContext.Rentals
            .Include(r => r.Items)
            .Include(r => r.Customer)
            .FirstOrDefaultAsync(r => r.Id == rentalId);

        retrievedRental.Should().NotBeNull("Rental should exist in database");
        retrievedRental!.TenantId.Should().Be(_testTenantId);
        retrievedRental.CustomerId.Should().Be(_testCustomerId);
        retrievedRental.Status.Should().Be(RentalStatus.Confirmed);
        retrievedRental.TotalAmount.Should().Be(totalAmount);
        retrievedRental.DepositAmount.Should().Be(depositAmount);
        retrievedRental.PaymentStatus.Should().Be(PaymentIntentStatus.Succeeded);
        retrievedRental.Items.Should().HaveCount(1);
        retrievedRental.Items.First().ProductId.Should().Be(_testProductId);
        retrievedRental.Items.First().Quantity.Should().Be(quantity);

        _output.WriteLine("✅ Rental successfully retrieved from database!");
        _output.WriteLine($"  - Customer: {retrievedRental.Customer?.FullName}");
        _output.WriteLine($"  - Items count: {retrievedRental.Items.Count}");
        _output.WriteLine($"  - Status: {retrievedRental.Status}");
    }

    [Fact]
    public async Task CreateRental_ShouldBeVisibleInMyRentalsQuery()
    {
        // Arrange
        var rentalId = Guid.NewGuid();
        var startDate = DateTime.UtcNow.AddDays(1);
        var endDate = DateTime.UtcNow.AddDays(2);

        var product = await _dbContext.Products.FindAsync(_testProductId);
        var pricePerDay = product!.DailyPrice;

        var rental = new Rental
        {
            Id = rentalId,
            TenantId = _testTenantId,
            CustomerId = _testCustomerId,
            StartDateUtc = startDate,
            EndDateUtc = endDate,
            Status = RentalStatus.Confirmed,
            TotalAmount = pricePerDay,
            DepositAmount = pricePerDay * 0.3m,
            PaymentIntentId = $"pi_test_{Guid.NewGuid():N}",
            PaymentStatus = PaymentIntentStatus.Succeeded,
            IdempotencyKey = $"test_{Guid.NewGuid():N}",
            CreatedAtUtc = DateTime.UtcNow,
            Notes = "My rentals query test"
        };

        rental.Items.Add(new RentalItem
        {
            Id = Guid.NewGuid(),
            RentalId = rentalId,
            ProductId = _testProductId,
            Quantity = 1,
            PricePerDay = pricePerDay,
            Subtotal = pricePerDay
        });

        _createdRentalIds.Add(rentalId);

        // Act
        _dbContext.Rentals.Add(rental);
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        // Query like "My Rentals" page would
        var myRentals = await _dbContext.Rentals
            .Where(r => r.CustomerId == _testCustomerId)
            .Include(r => r.Items)
                .ThenInclude(i => i.Product)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToListAsync();

        // Assert
        myRentals.Should().Contain(r => r.Id == rentalId, "New rental should appear in customer's rentals");
        
        var foundRental = myRentals.First(r => r.Id == rentalId);
        foundRental.Items.Should().NotBeEmpty();
        foundRental.Items.First().Product.Should().NotBeNull();

        _output.WriteLine($"✅ Rental visible in 'My Rentals' query!");
        _output.WriteLine($"  - Total rentals for customer: {myRentals.Count}");
        _output.WriteLine($"  - Found rental ID: {foundRental.Id}");
        _output.WriteLine($"  - Product: {foundRental.Items.First().Product?.Name}");
    }

    [Fact]
    public async Task CreateMultipleRentals_ShouldAllPersist()
    {
        // Arrange
        var rentalCount = 3;
        var createdIds = new List<Guid>();

        var product = await _dbContext.Products.FindAsync(_testProductId);
        var pricePerDay = product!.DailyPrice;

        // Act - Create multiple rentals
        for (int i = 0; i < rentalCount; i++)
        {
            var rentalId = Guid.NewGuid();
            var rental = new Rental
            {
                Id = rentalId,
                TenantId = _testTenantId,
                CustomerId = _testCustomerId,
                StartDateUtc = DateTime.UtcNow.AddDays(i + 1),
                EndDateUtc = DateTime.UtcNow.AddDays(i + 2),
                Status = RentalStatus.Confirmed,
                TotalAmount = pricePerDay,
                DepositAmount = pricePerDay * 0.3m,
                PaymentIntentId = $"pi_test_multi_{i}_{Guid.NewGuid():N}",
                PaymentStatus = PaymentIntentStatus.Succeeded,
                IdempotencyKey = $"test_multi_{i}_{Guid.NewGuid():N}",
                CreatedAtUtc = DateTime.UtcNow,
                Notes = $"Multi-rental test #{i + 1}"
            };

            rental.Items.Add(new RentalItem
            {
                Id = Guid.NewGuid(),
                RentalId = rentalId,
                ProductId = _testProductId,
                Quantity = 1,
                PricePerDay = pricePerDay,
                Subtotal = pricePerDay
            });

            _dbContext.Rentals.Add(rental);
            createdIds.Add(rentalId);
            _createdRentalIds.Add(rentalId);
        }

        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        // Assert
        foreach (var id in createdIds)
        {
            var exists = await _dbContext.Rentals.AnyAsync(r => r.Id == id);
            exists.Should().BeTrue($"Rental {id} should exist in database");
        }

        _output.WriteLine($"✅ All {rentalCount} rentals persisted successfully!");
        foreach (var id in createdIds)
        {
            _output.WriteLine($"  - {id}");
        }
    }
}
