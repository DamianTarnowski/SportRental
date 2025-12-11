using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;
using SportRental.Shared.Models;
using Xunit;
using Xunit.Abstractions;

namespace SportRental.Admin.Tests.Integration;

/// <summary>
/// Cross-application integration tests.
/// Verifies that rentals created by WASM client are visible in Admin panel and vice versa.
/// This simulates the real-world scenario where customers book through the website
/// and admins manage those bookings in the admin panel.
/// </summary>
public class CrossAppRentalIntegrationTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private ApplicationDbContext _dbContext = null!;
    private Guid _testTenantId;
    private string _testTenantName = string.Empty;
    private Guid _testCustomerId;
    private string _testCustomerEmail = string.Empty;
    private Guid _testProductId;
    private string _testProductName = string.Empty;
    private decimal _testProductPrice;
    private readonly List<Guid> _createdRentalIds = new();

    private const string ConnectionString = "Host=eduedu.postgres.database.azure.com;Database=sr;Username=synapsis;Password=HasloHaslo122@@@@;SSL Mode=Require";

    public CrossAppRentalIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        _dbContext = new ApplicationDbContext(options);

        // Get tenant
        var tenant = await _dbContext.Tenants.FirstOrDefaultAsync();
        _testTenantId = tenant!.Id;
        _testTenantName = tenant.Name ?? "Unknown";
        _output.WriteLine($"[CrossApp Test] Tenant: {_testTenantName} ({_testTenantId})");

        // Create unique test customer (simulating WASM client registration)
        var uniqueEmail = $"wasm-client-{Guid.NewGuid():N}@test.com";
        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            TenantId = _testTenantId,
            FullName = "WASM Test Customer",
            Email = uniqueEmail,
            PhoneNumber = "111222333",
            CreatedAtUtc = DateTime.UtcNow
        };
        _dbContext.Customers.Add(customer);
        await _dbContext.SaveChangesAsync();
        _testCustomerId = customer.Id;
        _testCustomerEmail = uniqueEmail;
        _output.WriteLine($"[CrossApp Test] Created WASM customer: {customer.FullName} ({customer.Email})");

        // Get product
        var product = await _dbContext.Products.FirstOrDefaultAsync(p => p.TenantId == _testTenantId && p.AvailableQuantity > 0);
        _testProductId = product!.Id;
        _testProductName = product.Name;
        _testProductPrice = product.DailyPrice;
        _output.WriteLine($"[CrossApp Test] Product: {_testProductName} ({_testProductPrice}/day)");
    }

    public async Task DisposeAsync()
    {
        // Cleanup rentals
        foreach (var rentalId in _createdRentalIds)
        {
            var rental = await _dbContext.Rentals
                .Include(r => r.Items)
                .FirstOrDefaultAsync(r => r.Id == rentalId);
            
            if (rental is not null)
            {
                _dbContext.RentalItems.RemoveRange(rental.Items);
                _dbContext.Rentals.Remove(rental);
            }
        }

        // Cleanup test customer
        var customer = await _dbContext.Customers.FirstOrDefaultAsync(c => c.Id == _testCustomerId);
        if (customer is not null)
        {
            _dbContext.Customers.Remove(customer);
        }

        await _dbContext.SaveChangesAsync();
        _output.WriteLine($"[CrossApp Test] Cleanup completed");
        await _dbContext.DisposeAsync();
    }

    /// <summary>
    /// MAIN TEST: Customer creates rental via WASM client → Admin sees it in panel.
    /// This is the core business flow we need to verify.
    /// </summary>
    [Fact]
    public async Task WasmClientCreatesRental_AdminPanelSeesIt()
    {
        _output.WriteLine("\n========== WASM CLIENT → ADMIN PANEL TEST ==========\n");

        // ============ STEP 1: WASM Client creates rental (simulating Stripe webhook) ============
        _output.WriteLine("[WASM Client] Customer browsing products...");
        _output.WriteLine($"[WASM Client] Selected: {_testProductName}");
        _output.WriteLine("[WASM Client] Proceeding to checkout...");
        _output.WriteLine("[WASM Client] Payment successful via Stripe!");

        var rentalId = Guid.NewGuid();
        var startDate = DateTime.UtcNow.AddDays(1);
        var endDate = DateTime.UtcNow.AddDays(4);
        var days = 3;
        var quantity = 1;
        var totalAmount = _testProductPrice * quantity * days;
        var depositAmount = totalAmount * 0.3m;
        var paymentIntentId = $"pi_wasm_test_{Guid.NewGuid():N}";

        // This simulates what StripeWebhookEndpoints.CreateRentalForTenantAsync does
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
            PaymentIntentId = paymentIntentId,
            PaymentStatus = PaymentIntentStatus.Succeeded,
            IdempotencyKey = $"checkout:{Guid.NewGuid():N}:{_testTenantId}",
            CreatedAtUtc = DateTime.UtcNow,
            Notes = "Rental created via WASM client checkout"
        };

        rental.Items.Add(new RentalItem
        {
            Id = Guid.NewGuid(),
            RentalId = rentalId,
            ProductId = _testProductId,
            Quantity = quantity,
            PricePerDay = _testProductPrice,
            Subtotal = totalAmount
        });

        _dbContext.Rentals.Add(rental);
        await _dbContext.SaveChangesAsync();
        _createdRentalIds.Add(rentalId);

        _output.WriteLine($"[WASM Client] ✅ Rental created: {rentalId}");
        _output.WriteLine($"[WASM Client]    Period: {startDate:yyyy-MM-dd} - {endDate:yyyy-MM-dd}");
        _output.WriteLine($"[WASM Client]    Total: {totalAmount:F2} PLN");
        _output.WriteLine($"[WASM Client]    Payment: {paymentIntentId}");

        // Clear EF cache to simulate separate application
        _dbContext.ChangeTracker.Clear();

        // ============ STEP 2: Admin Panel queries rentals ============
        _output.WriteLine("\n[Admin Panel] Admin logging in...");
        _output.WriteLine($"[Admin Panel] Loading rentals for tenant: {_testTenantName}");

        // This simulates GET /api/my-rentals or admin rentals list
        var adminRentalsQuery = await _dbContext.Rentals
            .AsNoTracking()
            .Include(r => r.Items)
                .ThenInclude(i => i.Product)
            .Include(r => r.Customer)
            .Where(r => r.TenantId == _testTenantId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToListAsync();

        _output.WriteLine($"[Admin Panel] Found {adminRentalsQuery.Count} total rentals");

        // ============ STEP 3: Verify the WASM rental is visible ============
        var wasmRental = adminRentalsQuery.FirstOrDefault(r => r.Id == rentalId);

        wasmRental.Should().NotBeNull("WASM client rental should be visible in Admin panel");
        wasmRental!.Customer.Should().NotBeNull("Customer data should be loaded");
        wasmRental.Customer!.Email.Should().Be(_testCustomerEmail);
        wasmRental.Items.Should().HaveCount(1);
        wasmRental.Items.First().Product.Should().NotBeNull("Product data should be loaded");
        wasmRental.PaymentStatus.Should().Be(PaymentIntentStatus.Succeeded);
        wasmRental.Status.Should().Be(RentalStatus.Confirmed);

        _output.WriteLine($"\n[Admin Panel] ✅ WASM rental found!");
        _output.WriteLine($"[Admin Panel]    Rental ID: {wasmRental.Id}");
        _output.WriteLine($"[Admin Panel]    Customer: {wasmRental.Customer.FullName} ({wasmRental.Customer.Email})");
        _output.WriteLine($"[Admin Panel]    Product: {wasmRental.Items.First().Product?.Name}");
        _output.WriteLine($"[Admin Panel]    Status: {wasmRental.Status}");
        _output.WriteLine($"[Admin Panel]    Payment: {wasmRental.PaymentStatus}");
        _output.WriteLine($"[Admin Panel]    Total: {wasmRental.TotalAmount:F2} PLN");

        _output.WriteLine("\n========== TEST PASSED ✅ ==========\n");
    }

    /// <summary>
    /// Test: Admin can update rental created by WASM client.
    /// </summary>
    [Fact]
    public async Task AdminCanModifyWasmClientRental()
    {
        _output.WriteLine("\n========== ADMIN MODIFIES WASM RENTAL TEST ==========\n");

        // WASM creates rental
        var rentalId = Guid.NewGuid();
        var rental = new Rental
        {
            Id = rentalId,
            TenantId = _testTenantId,
            CustomerId = _testCustomerId,
            StartDateUtc = DateTime.UtcNow.AddDays(5),
            EndDateUtc = DateTime.UtcNow.AddDays(7),
            Status = RentalStatus.Confirmed,
            TotalAmount = _testProductPrice * 2,
            PaymentIntentId = $"pi_modify_test_{Guid.NewGuid():N}",
            PaymentStatus = PaymentIntentStatus.Succeeded,
            CreatedAtUtc = DateTime.UtcNow
        };
        rental.Items.Add(new RentalItem
        {
            Id = Guid.NewGuid(),
            RentalId = rentalId,
            ProductId = _testProductId,
            Quantity = 1,
            PricePerDay = _testProductPrice,
            Subtotal = _testProductPrice * 2
        });

        _dbContext.Rentals.Add(rental);
        await _dbContext.SaveChangesAsync();
        _createdRentalIds.Add(rentalId);
        _output.WriteLine($"[WASM] Rental created: {rentalId}");

        _dbContext.ChangeTracker.Clear();

        // Admin modifies rental (adds note)
        var adminRental = await _dbContext.Rentals.FirstAsync(r => r.Id == rentalId);
        adminRental.Notes = "Admin note: Customer called to confirm pickup time";
        await _dbContext.SaveChangesAsync();
        _output.WriteLine("[Admin] Added note to rental");

        _dbContext.ChangeTracker.Clear();

        // Verify modification persisted
        var updatedRental = await _dbContext.Rentals.AsNoTracking().FirstAsync(r => r.Id == rentalId);
        updatedRental.Notes.Should().Contain("Admin note");

        _output.WriteLine($"[Verify] ✅ Admin modification saved: {updatedRental.Notes}");
    }

    /// <summary>
    /// Test: Admin cancels WASM client rental.
    /// </summary>
    [Fact]
    public async Task AdminCanCancelWasmClientRental()
    {
        _output.WriteLine("\n========== ADMIN CANCELS WASM RENTAL TEST ==========\n");

        // WASM creates rental
        var rentalId = Guid.NewGuid();
        var rental = new Rental
        {
            Id = rentalId,
            TenantId = _testTenantId,
            CustomerId = _testCustomerId,
            StartDateUtc = DateTime.UtcNow.AddDays(10),
            EndDateUtc = DateTime.UtcNow.AddDays(12),
            Status = RentalStatus.Confirmed,
            TotalAmount = _testProductPrice * 2,
            PaymentIntentId = $"pi_cancel_test_{Guid.NewGuid():N}",
            PaymentStatus = PaymentIntentStatus.Succeeded,
            CreatedAtUtc = DateTime.UtcNow
        };
        rental.Items.Add(new RentalItem
        {
            Id = Guid.NewGuid(),
            RentalId = rentalId,
            ProductId = _testProductId,
            Quantity = 1,
            PricePerDay = _testProductPrice,
            Subtotal = _testProductPrice * 2
        });

        _dbContext.Rentals.Add(rental);
        await _dbContext.SaveChangesAsync();
        _createdRentalIds.Add(rentalId);
        _output.WriteLine($"[WASM] Rental created with status: {rental.Status}");

        _dbContext.ChangeTracker.Clear();

        // Admin cancels (simulating DELETE /api/rentals/{id})
        var adminRental = await _dbContext.Rentals.FirstAsync(r => r.Id == rentalId);
        adminRental.Status = RentalStatus.Cancelled;
        await _dbContext.SaveChangesAsync();
        _output.WriteLine("[Admin] Rental cancelled");

        _dbContext.ChangeTracker.Clear();

        // Verify cancellation
        var cancelledRental = await _dbContext.Rentals.AsNoTracking().FirstAsync(r => r.Id == rentalId);
        cancelledRental.Status.Should().Be(RentalStatus.Cancelled);

        _output.WriteLine($"[Verify] ✅ Rental status: {cancelledRental.Status}");
    }

    /// <summary>
    /// Test: WASM client can see their rental in "My Rentals" after admin processes it.
    /// </summary>
    [Fact]
    public async Task WasmClientSeesRentalAfterAdminProcessing()
    {
        _output.WriteLine("\n========== WASM SEES ADMIN-PROCESSED RENTAL TEST ==========\n");

        // WASM creates rental
        var rentalId = Guid.NewGuid();
        var rental = new Rental
        {
            Id = rentalId,
            TenantId = _testTenantId,
            CustomerId = _testCustomerId,
            StartDateUtc = DateTime.UtcNow.AddDays(15),
            EndDateUtc = DateTime.UtcNow.AddDays(17),
            Status = RentalStatus.Confirmed,
            TotalAmount = _testProductPrice * 2,
            PaymentIntentId = $"pi_myrentals_test_{Guid.NewGuid():N}",
            PaymentStatus = PaymentIntentStatus.Succeeded,
            CreatedAtUtc = DateTime.UtcNow
        };
        rental.Items.Add(new RentalItem
        {
            Id = Guid.NewGuid(),
            RentalId = rentalId,
            ProductId = _testProductId,
            Quantity = 1,
            PricePerDay = _testProductPrice,
            Subtotal = _testProductPrice * 2
        });

        _dbContext.Rentals.Add(rental);
        await _dbContext.SaveChangesAsync();
        _createdRentalIds.Add(rentalId);
        _output.WriteLine($"[WASM] Rental created: {rentalId}");

        _dbContext.ChangeTracker.Clear();

        // Admin adds contract URL (simulating contract generation)
        var adminRental = await _dbContext.Rentals.FirstAsync(r => r.Id == rentalId);
        adminRental.ContractUrl = $"https://storage.example.com/contracts/{rentalId}.pdf";
        adminRental.IsEmailSent = true;
        await _dbContext.SaveChangesAsync();
        _output.WriteLine("[Admin] Contract generated and email sent");

        _dbContext.ChangeTracker.Clear();

        // WASM client checks "My Rentals" (simulating customer viewing their rentals)
        var myRentals = await _dbContext.Rentals
            .AsNoTracking()
            .Include(r => r.Items)
                .ThenInclude(i => i.Product)
            .Where(r => r.CustomerId == _testCustomerId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToListAsync();

        var myRental = myRentals.FirstOrDefault(r => r.Id == rentalId);
        
        myRental.Should().NotBeNull("Customer should see their rental");
        myRental!.ContractUrl.Should().NotBeNullOrEmpty("Contract URL should be set by admin");
        myRental.IsEmailSent.Should().BeTrue("Email sent flag should be set");

        _output.WriteLine($"[WASM] ✅ Customer sees rental in 'My Rentals'");
        _output.WriteLine($"[WASM]    Contract: {myRental.ContractUrl}");
        _output.WriteLine($"[WASM]    Email sent: {myRental.IsEmailSent}");
    }

    /// <summary>
    /// Test: Multiple WASM clients create rentals, all visible in Admin.
    /// </summary>
    [Fact]
    public async Task MultipleWasmClients_AllVisibleInAdmin()
    {
        _output.WriteLine("\n========== MULTIPLE WASM CLIENTS TEST ==========\n");

        var clientRentals = new List<(Guid RentalId, string CustomerName)>();

        // Create 3 different customers with rentals
        for (int i = 1; i <= 3; i++)
        {
            var customerId = Guid.NewGuid();
            var customer = new Customer
            {
                Id = customerId,
                TenantId = _testTenantId,
                FullName = $"WASM Customer {i}",
                Email = $"wasm-multi-{i}-{Guid.NewGuid():N}@test.com",
                CreatedAtUtc = DateTime.UtcNow
            };
            _dbContext.Customers.Add(customer);

            var rentalId = Guid.NewGuid();
            var rental = new Rental
            {
                Id = rentalId,
                TenantId = _testTenantId,
                CustomerId = customerId,
                StartDateUtc = DateTime.UtcNow.AddDays(20 + i),
                EndDateUtc = DateTime.UtcNow.AddDays(21 + i),
                Status = RentalStatus.Confirmed,
                TotalAmount = _testProductPrice * i,
                PaymentIntentId = $"pi_multi_{i}_{Guid.NewGuid():N}",
                PaymentStatus = PaymentIntentStatus.Succeeded,
                CreatedAtUtc = DateTime.UtcNow.AddMinutes(-i) // Different times
            };
            rental.Items.Add(new RentalItem
            {
                Id = Guid.NewGuid(),
                RentalId = rentalId,
                ProductId = _testProductId,
                Quantity = i,
                PricePerDay = _testProductPrice,
                Subtotal = _testProductPrice * i
            });

            _dbContext.Rentals.Add(rental);
            _createdRentalIds.Add(rentalId);
            clientRentals.Add((rentalId, customer.FullName));

            _output.WriteLine($"[WASM Client {i}] Created rental: {rentalId}");
        }

        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        // Admin queries all rentals
        var adminRentals = await _dbContext.Rentals
            .AsNoTracking()
            .Include(r => r.Customer)
            .Where(r => r.TenantId == _testTenantId)
            .ToListAsync();

        // Verify all WASM rentals are visible
        foreach (var (rentalId, customerName) in clientRentals)
        {
            var found = adminRentals.FirstOrDefault(r => r.Id == rentalId);
            found.Should().NotBeNull($"Rental from {customerName} should be visible");
            _output.WriteLine($"[Admin] ✅ Found rental from {found!.Customer?.FullName}");
        }

        _output.WriteLine($"\n[Admin] Total rentals visible: {adminRentals.Count}");

        // Cleanup: first remove rentals, then customers
        var extraCustomerIds = clientRentals.Select(c => 
            _dbContext.Rentals.AsNoTracking().First(r => r.Id == c.RentalId).CustomerId)
            .Where(id => id != _testCustomerId)
            .Distinct()
            .ToList();

        // Remove rental items and rentals first
        foreach (var rentalId in _createdRentalIds.ToList())
        {
            var rental = await _dbContext.Rentals
                .Include(r => r.Items)
                .FirstOrDefaultAsync(r => r.Id == rentalId);
            if (rental != null)
            {
                _dbContext.RentalItems.RemoveRange(rental.Items);
                _dbContext.Rentals.Remove(rental);
            }
        }
        await _dbContext.SaveChangesAsync();
        _createdRentalIds.Clear(); // Prevent double cleanup in DisposeAsync

        // Now safe to remove extra customers
        var extraCustomers = await _dbContext.Customers
            .Where(c => extraCustomerIds.Contains(c.Id))
            .ToListAsync();
        _dbContext.Customers.RemoveRange(extraCustomers);
        await _dbContext.SaveChangesAsync();
        
        _output.WriteLine($"[Cleanup] Removed {extraCustomers.Count} extra test customers");
    }
}
