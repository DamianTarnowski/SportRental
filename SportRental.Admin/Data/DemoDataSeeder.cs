using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;
using SportRental.Shared.Identity;

namespace SportRental.Admin.Data;

/// <summary>
/// Ręczne zasianie danych demo na potrzeby prezentacji UI (produkty, klienci, wynajmy).
/// Uruchamiane z linii poleceń: dotnet run --project SportRental.Admin -- --seed-demo --seed-email=hdtdtr@gmail.com
/// Nie jest wykonywane automatycznie przy starcie aplikacji.
/// </summary>
public class DemoDataSeeder
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole<Guid>> _roleManager;
    private readonly ILogger<DemoDataSeeder> _logger;

    public DemoDataSeeder(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole<Guid>> roleManager,
        ILogger<DemoDataSeeder> logger)
    {
        _dbFactory = dbFactory;
        _userManager = userManager;
        _roleManager = roleManager;
        _logger = logger;
    }

    public async Task SeedAsync(string userEmail, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("🌱 Seeding danych demo dla użytkownika {UserEmail}", userEmail);

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        // Ustal tenant
        var tenantId = await EnsureTenantAsync(db, cancellationToken);

        // Zapewnij użytkownika i role
        await EnsureUserAsync(userEmail, tenantId, cancellationToken);

        // Jeśli są już wynajmy dla tego tenant – nie dublujemy
        var rentalsExisting = await db.Rentals.IgnoreQueryFilters()
            .AnyAsync(r => r.TenantId == tenantId, cancellationToken);
        if (rentalsExisting)
        {
            _logger.LogInformation("➡️  Wynajmy już istnieją dla tenant {TenantId}, pomijam seeding.", tenantId);
            return;
        }

        // Produkty
        db.SetTenant(tenantId);
        var products = await EnsureProductsAsync(db, tenantId, cancellationToken);

        // Klienci
        var customers = await EnsureCustomersAsync(db, tenantId, cancellationToken);

        // Wynajmy
        await SeedRentalsAsync(db, tenantId, products, customers, cancellationToken);

        _logger.LogInformation("✅ Zakończono seeding danych demo.");
    }

    private async Task<Guid> EnsureTenantAsync(ApplicationDbContext db, CancellationToken ct)
    {
        var tenant = await db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(ct);
        if (tenant != null)
            return tenant.Id;

        tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "Demo Rental",
            PrimaryColorHex = "#3949ab",
            SecondaryColorHex = "#00bcd4",
            CreatedAtUtc = DateTime.UtcNow
        };
        await db.Tenants.AddAsync(tenant, ct);
        await db.SaveChangesAsync(ct);
        _logger.LogInformation("➕ Utworzono tenant {Tenant} ({TenantId})", tenant.Name, tenant.Id);
        return tenant.Id;
    }

    private async Task EnsureUserAsync(string email, Guid tenantId, CancellationToken ct)
    {
        string[] roles = [RoleNames.SuperAdmin, RoleNames.Owner, RoleNames.Client];
        foreach (var role in roles)
        {
            if (!await _roleManager.RoleExistsAsync(role))
                await _roleManager.CreateAsync(new IdentityRole<Guid>(role));
        }

        var user = await _userManager.FindByEmailAsync(email);
        if (user == null)
        {
            user = new ApplicationUser
            {
                Email = email,
                UserName = email,
                EmailConfirmed = true,
                TenantId = tenantId
            };
            var create = await _userManager.CreateAsync(user, "Owner123!");
            if (!create.Succeeded)
                throw new InvalidOperationException("Nie udało się utworzyć użytkownika: " +
                                                    string.Join("; ", create.Errors.Select(e => e.Description)));
            _logger.LogInformation("➕ Utworzono użytkownika {Email}", email);
        }
        else if (user.TenantId == null || user.TenantId == Guid.Empty)
        {
            user.TenantId = tenantId;
            await _userManager.UpdateAsync(user);
        }

        foreach (var role in roles)
        {
            if (!await _userManager.IsInRoleAsync(user, role))
                await _userManager.AddToRoleAsync(user, role);
        }
    }

    private async Task<List<Product>> EnsureProductsAsync(ApplicationDbContext db, Guid tenantId, CancellationToken ct)
    {
        var existing = await db.Products.IgnoreQueryFilters()
            .Where(p => p.TenantId == tenantId)
            .ToListAsync(ct);
        if (existing.Any())
            return existing;

        var now = DateTime.UtcNow;
        var demoProducts = new List<Product>
        {
            new() { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Narty All-Mountain", Sku = "SKI-ALL-001", Category = "Narty", DailyPrice = 120, AvailableQuantity = 8, CreatedAtUtc = now, Available = true, IsActive = true },
            new() { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Deska Snowboard", Sku = "SNOW-002", Category = "Snowboard", DailyPrice = 110, AvailableQuantity = 5, CreatedAtUtc = now, Available = true, IsActive = true },
            new() { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Buty narciarskie", Sku = "BOOT-003", Category = "Buty", DailyPrice = 60, AvailableQuantity = 10, CreatedAtUtc = now, Available = true, IsActive = true },
            new() { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Kijki trekkingowe", Sku = "POLE-004", Category = "Akcesoria", DailyPrice = 25, AvailableQuantity = 20, CreatedAtUtc = now, Available = true, IsActive = true }
        };

        await db.Products.AddRangeAsync(demoProducts, ct);
        await db.SaveChangesAsync(ct);
        _logger.LogInformation("➕ Dodano produkty demo: {Count}", demoProducts.Count);
        return demoProducts;
    }

    private async Task<List<Customer>> EnsureCustomersAsync(ApplicationDbContext db, Guid tenantId, CancellationToken ct)
    {
        var existing = await db.Customers.IgnoreQueryFilters()
            .Where(c => c.TenantId == tenantId)
            .ToListAsync(ct);
        if (existing.Any())
            return existing;

        var demoCustomers = new List<Customer>
        {
            new() { Id = Guid.NewGuid(), TenantId = tenantId, FullName = "Jan Kowalski", Email = "jan.kowalski@example.com", PhoneNumber = "+48555444333", CreatedAtUtc = DateTime.UtcNow },
            new() { Id = Guid.NewGuid(), TenantId = tenantId, FullName = "Anna Nowak", Email = "anna.nowak@example.com", PhoneNumber = "+48555111222", CreatedAtUtc = DateTime.UtcNow },
            new() { Id = Guid.NewGuid(), TenantId = tenantId, FullName = "Piotr Zieliński", Email = "piotr.zielinski@example.com", PhoneNumber = "+48555999111", CreatedAtUtc = DateTime.UtcNow }
        };

        await db.Customers.AddRangeAsync(demoCustomers, ct);
        await db.SaveChangesAsync(ct);
        _logger.LogInformation("➕ Dodano klientów demo: {Count}", demoCustomers.Count);
        return demoCustomers;
    }

    private async Task SeedRentalsAsync(ApplicationDbContext db, Guid tenantId, List<Product> products, List<Customer> customers, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        // Przygotuj kilka koszyków
        var rentals = new List<Rental>
        {
            CreateRental(tenantId, customers[0], now.AddDays(-3), now.AddDays(-1), RentalStatus.Completed, products[0], 2),
            CreateRental(tenantId, customers[1], now.AddDays(-1), now.AddDays(1), RentalStatus.Active, products[1], 1, products[3], 2),
            CreateRental(tenantId, customers[2], now.AddDays(1), now.AddDays(4), RentalStatus.Confirmed, products[2], 3),
            CreateRental(tenantId, customers[0], now.AddDays(5), now.AddDays(7), RentalStatus.Pending, products[3], 4),
            CreateRental(tenantId, customers[1], now.AddHours(-4), now.AddHours(6), RentalStatus.Active, products[0], 1, products[2], 1)
        };

        await db.Rentals.AddRangeAsync(rentals, ct);
        await db.SaveChangesAsync(ct);
        _logger.LogInformation("➕ Dodano wynajmy demo: {Count}", rentals.Count);
    }

    private static Rental CreateRental(Guid tenantId, Customer customer, DateTime start, DateTime end, RentalStatus status, Product p1, int qty1, Product? p2 = null, int qty2 = 0)
    {
        var rental = new Rental
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CustomerId = customer.Id,
            StartDateUtc = start,
            EndDateUtc = end,
            Status = status,
            CreatedAtUtc = DateTime.UtcNow,
            TotalAmount = 0m,
            PaymentIntentId = null, // baza ma kolumnę uuid z wcześniejszych migracji – zostawiamy null
            PaymentStatus = status switch
            {
                RentalStatus.Completed or RentalStatus.Active or RentalStatus.Confirmed => "succeeded",
                RentalStatus.Pending => "requires_payment_method",
                _ => "demo"
            },
            Items = new List<RentalItem>()
        };

        rental.Items.Add(new RentalItem
        {
            Id = Guid.NewGuid(),
            RentalId = rental.Id,
            ProductId = p1.Id,
            Quantity = qty1,
            PricePerDay = p1.DailyPrice,
            Subtotal = p1.DailyPrice * qty1 * Math.Max(1, (end - start).Days)
        });

        if (p2 != null && qty2 > 0)
        {
            rental.Items.Add(new RentalItem
            {
                Id = Guid.NewGuid(),
                RentalId = rental.Id,
                ProductId = p2.Id,
                Quantity = qty2,
                PricePerDay = p2.DailyPrice,
            Subtotal = p2.DailyPrice * qty2 * Math.Max(1, (end - start).Days)
            });
        }

        rental.TotalAmount = rental.Items.Sum(i => i.Subtotal);
        return rental;
    }
}
