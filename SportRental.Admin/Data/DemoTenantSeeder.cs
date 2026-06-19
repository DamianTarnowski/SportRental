using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;
using SportRental.Shared.Identity;

namespace SportRental.Admin.Data;

/// Idempotentnie zapewnia tenant "Demo Wypożyczalnia" + użytkownika demo@rentspot.eu / Demo123!
/// + przykładowe produkty. Wywoływany z /Account/Demo aby umożliwić one-click try-out.
public class DemoTenantSeeder
{
    private const string DemoTenantName = "Demo Wypożyczalnia";
    public const string DemoUserEmail = "demo@rentspot.eu";
    public const string DemoUserPassword = "DemoRentSpot2026!";

    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole<Guid>> _roleManager;
    private readonly ILogger<DemoTenantSeeder> _logger;

    public DemoTenantSeeder(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole<Guid>> roleManager,
        ILogger<DemoTenantSeeder> logger)
    {
        _dbFactory = dbFactory;
        _userManager = userManager;
        _roleManager = roleManager;
        _logger = logger;
    }

    public async Task<ApplicationUser> EnsureAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var tenant = await db.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Name == DemoTenantName, ct);

        if (tenant == null)
        {
            tenant = new Tenant
            {
                Id = Guid.NewGuid(),
                Name = DemoTenantName,
                PrimaryColorHex = "#F96167",
                SecondaryColorHex = "#1B2350",
                CreatedAtUtc = DateTime.UtcNow
            };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("Created demo tenant {TenantId}", tenant.Id);
        }

        // Zapewnij role
        foreach (var role in new[] { RoleNames.SuperAdmin, RoleNames.Owner, RoleNames.Employee, RoleNames.Client })
        {
            if (!await _roleManager.RoleExistsAsync(role))
                await _roleManager.CreateAsync(new IdentityRole<Guid>(role));
        }

        var user = await _userManager.FindByEmailAsync(DemoUserEmail);
        if (user == null)
        {
            user = new ApplicationUser
            {
                Email = DemoUserEmail,
                UserName = DemoUserEmail,
                EmailConfirmed = true,
                TenantId = tenant.Id
            };
            var create = await _userManager.CreateAsync(user, DemoUserPassword);
            if (!create.Succeeded)
                throw new InvalidOperationException("Demo user create failed: " +
                    string.Join("; ", create.Errors.Select(e => e.Description)));
            _logger.LogInformation("Created demo user {Email}", DemoUserEmail);
        }
        else if (user.TenantId != tenant.Id)
        {
            user.TenantId = tenant.Id;
            await _userManager.UpdateAsync(user);
        }

        if (!await _userManager.IsInRoleAsync(user, RoleNames.Owner))
            await _userManager.AddToRoleAsync(user, RoleNames.Owner);

        // Mapowanie TenantUser (jeśli używane)
        var hasTenantUser = await db.Set<TenantUser>()
            .IgnoreQueryFilters()
            .AnyAsync(tu => tu.TenantId == tenant.Id && tu.UserId == user.Id, ct);
        if (!hasTenantUser)
        {
            db.Set<TenantUser>().Add(new TenantUser
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.Id,
                UserId = user.Id,
                DisplayName = "Demo Owner",
                Role = RoleNames.Owner
            });
            await db.SaveChangesAsync(ct);
        }

        // Produkty
        var productCount = await db.Products
            .IgnoreQueryFilters()
            .CountAsync(p => p.TenantId == tenant.Id, ct);

        if (productCount == 0)
        {
            var now = DateTime.UtcNow;
            var products = new[]
            {
                NewProduct(tenant.Id, "Rower MTB Trek Marlin 6", "BIKE-MTB-001", "Rowery", 80m, 4, now, "Trek", "Marlin 6"),
                NewProduct(tenant.Id, "Rower trekkingowy Kross Trans", "BIKE-TRK-002", "Rowery", 60m, 3, now, "Kross", "Trans 5.0"),
                NewProduct(tenant.Id, "Kajak 2-os Pelican Argo", "KAY-002-001", "Kajaki", 120m, 2, now, "Pelican", "Argo 100XR"),
                NewProduct(tenant.Id, "Deska SUP Aqua Marina Beast", "SUP-001", "SUP", 90m, 3, now, "Aqua Marina", "Beast 10'6"),
                NewProduct(tenant.Id, "Narty all-mountain Atomic Vantage", "SKI-AM-001", "Narty", 130m, 6, now, "Atomic", "Vantage 90"),
                NewProduct(tenant.Id, "Buty narciarskie Tecnica Mach1", "BOOT-SKI-001", "Buty", 50m, 8, now, "Tecnica", "Mach1 MV"),
                NewProduct(tenant.Id, "Kask narciarski Salomon", "HELM-SKI-001", "Akcesoria", 20m, 12, now, "Salomon", "Driver"),
                NewProduct(tenant.Id, "Kijki trekkingowe Black Diamond", "POLE-TRK-001", "Akcesoria", 15m, 20, now, "Black Diamond", "Trail Pro")
            };
            db.Products.AddRange(products);

            // Klienci przykładowi
            db.Customers.AddRange(
                new Customer { Id = Guid.NewGuid(), TenantId = tenant.Id, FullName = "Jan Kowalski", Email = "jan.kowalski@example.com", PhoneNumber = "+48555444333", CreatedAtUtc = now },
                new Customer { Id = Guid.NewGuid(), TenantId = tenant.Id, FullName = "Anna Nowak", Email = "anna.nowak@example.com", PhoneNumber = "+48555111222", CreatedAtUtc = now }
            );

            await db.SaveChangesAsync(ct);
            _logger.LogInformation("Seeded {Count} demo products + 2 customers for tenant {TenantId}", products.Length, tenant.Id);
        }

        return user;
    }

    private static Product NewProduct(Guid tenantId, string name, string sku, string category, decimal price, int qty, DateTime now, string producer, string model) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name,
            Sku = sku,
            Category = category,
            Producer = producer,
            Model = model,
            DailyPrice = price,
            AvailableQuantity = qty,
            Available = true,
            IsActive = true,
            CreatedAtUtc = now
        };
}
