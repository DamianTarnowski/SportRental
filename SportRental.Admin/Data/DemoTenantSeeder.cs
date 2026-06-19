using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;
using SportRental.Shared.Identity;

namespace SportRental.Admin.Data;

/// Tworzy ŚWIEŻY izolowany tenant demo per session "Wypróbuj demo".
/// Każdy klik → nowy Guid tenant + nowy user demo+<shortId>@rentspot.eu + bogaty seed.
/// Po DemoExpiresAtUtc (default +8h) background cleanup usuwa cały tenant.
/// User flag IsDemoUser=true blokuje SMS/Email do realnych odbiorców.
public class DemoTenantSeeder
{
    public const string DemoUserPassword = "DemoRentSpot2026!";
    public static readonly TimeSpan DemoTtl = TimeSpan.FromHours(8);

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

    public async Task<ApplicationUser> CreateFreshAsync(CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;
        var shortId = Guid.NewGuid().ToString()[..8];

        // 1. TENANT — świeży per session
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = $"Demo Wypożyczalnia [{shortId.ToUpper()}]",
            PrimaryColorHex = "#F96167",
            SecondaryColorHex = "#1B2350",
            CreatedAtUtc = now,
            IsDemo = true,
            DemoExpiresAtUtc = now.Add(DemoTtl)
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync(ct);

        // 2. ROLES — idempotent (raz na DB lifetime)
        foreach (var role in new[] { RoleNames.SuperAdmin, RoleNames.Owner, RoleNames.Employee, RoleNames.Client })
        {
            if (!await _roleManager.RoleExistsAsync(role))
                await _roleManager.CreateAsync(new IdentityRole<Guid>(role));
        }

        // 3. USER — unikalny email per session
        var userEmail = $"demo+{shortId}@rentspot.eu";
        var user = new ApplicationUser
        {
            Email = userEmail,
            UserName = userEmail,
            EmailConfirmed = true,
            TenantId = tenant.Id,
            IsDemoUser = true
        };
        var create = await _userManager.CreateAsync(user, DemoUserPassword);
        if (!create.Succeeded)
            throw new InvalidOperationException("Demo user create failed: " +
                string.Join("; ", create.Errors.Select(e => e.Description)));

        await _userManager.AddToRoleAsync(user, RoleNames.Owner);

        db.Set<TenantUser>().Add(new TenantUser
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            UserId = user.Id,
            DisplayName = "Demo Owner",
            Role = RoleNames.Owner
        });

        // 4. PRODUKTY — 12 sztuk różnych kategorii
        var products = new[]
        {
            P(tenant.Id, "Rower MTB Trek Marlin 6", "BIKE-MTB-001", "Rowery", 80m, 4, "Trek", "Marlin 6", "Lekki rower górski idealny na leśne szlaki. Hamulce hydrauliczne, 21 biegów."),
            P(tenant.Id, "Rower trekkingowy Kross Trans 5.0", "BIKE-TRK-002", "Rowery", 60m, 3, "Kross", "Trans 5.0", "Wszechstronny rower z amortyzowanym widelcem. Idealny do miasta i na dłuższe trasy."),
            P(tenant.Id, "Rower elektryczny Romet e-Wagant", "EBIKE-001", "Rowery", 180m, 2, "Romet", "e-Wagant", "E-bike z silnikiem centralnym Bosch, zasięg do 100km. Wspomaganie 4 trybami."),
            P(tenant.Id, "Kajak 2-osobowy Pelican Argo 100XR", "KAY-002-001", "Kajaki", 120m, 2, "Pelican", "Argo 100XR", "Stabilny kajak rekreacyjny dla 2 osób. Materiał RAM-X, w zestawie wiosła i kamizelki."),
            P(tenant.Id, "Kajak turystyczny Wave 1-os", "KAY-001-001", "Kajaki", 80m, 5, "Wave Sport", "Tour 280", "Lekki kajak rekreacyjny na jeziora i spokojne rzeki. Wymarzony na popołudniowe wyprawy."),
            P(tenant.Id, "Deska SUP Aqua Marina Beast 10'6", "SUP-001", "SUP", 90m, 3, "Aqua Marina", "Beast 10'6", "Pompowany SUP all-around z plecakiem i pompą. Idealny dla początkujących i średnio-zaawansowanych."),
            P(tenant.Id, "Narty all-mountain Atomic Vantage 90", "SKI-AM-001", "Narty", 130m, 6, "Atomic", "Vantage 90 Ti", "Wszechstronne narty na cały stok. Tytan + drewno olchowe, długości 168-184cm."),
            P(tenant.Id, "Snowboard Burton Custom 156", "SNB-001", "Snowboard", 110m, 4, "Burton", "Custom 156", "Klasyczny all-mountain snowboard. Direction twin, dla zaawansowanych."),
            P(tenant.Id, "Buty narciarskie Tecnica Mach1 MV", "BOOT-SKI-001", "Buty", 50m, 8, "Tecnica", "Mach1 MV", "Wygodne buty all-mountain. Flex 110, ostatnia szerokość 100mm."),
            P(tenant.Id, "Buty snowboardowe ThirtyTwo TM-2", "BOOT-SNB-001", "Buty", 45m, 6, "ThirtyTwo", "TM-2", "Średnio-twardy boot dla freestyle i all-mountain."),
            P(tenant.Id, "Kask narciarski Salomon Driver", "HELM-SKI-001", "Kaski", 20m, 12, "Salomon", "Driver", "Lekki kask z systemem MIPS. Regulacja 360°, certyfikat EN 1077."),
            P(tenant.Id, "Kijki trekkingowe Black Diamond Trail Pro", "POLE-TRK-001", "Akcesoria", 15m, 20, "Black Diamond", "Trail Pro", "Aluminiowe kijki trekkingowe z systemem FlickLock. Składane, regulacja 100-140cm.")
        };
        db.Products.AddRange(products);

        // 5. KLIENCI — 7 z różnymi trust levels
        var c1 = NewCustomer(tenant.Id, "Anna Nowak", "anna.nowak@example.com", "+48555111222", "98050512345", CustomerTrustLevel.Good, now.AddMonths(-6));
        var c2 = NewCustomer(tenant.Id, "Jan Kowalski", "jan.kowalski@example.com", "+48555444333", "85120567890", CustomerTrustLevel.Good, now.AddMonths(-4));
        var c3 = NewCustomer(tenant.Id, "Maria Wiśniewska", "maria.w@example.com", "+48666333444", null, CustomerTrustLevel.Good, now.AddMonths(-2));
        var c4 = NewCustomer(tenant.Id, "Piotr Lewandowski", "piotr.lew@example.com", "+48555888999", null, CustomerTrustLevel.Unverified, now.AddDays(-12));
        var c5 = NewCustomer(tenant.Id, "Katarzyna Zielińska", "k.zielinska@example.com", "+48555222111", null, CustomerTrustLevel.Unverified, now.AddDays(-5));
        var c6 = NewCustomer(tenant.Id, "Tomasz Wojciechowski", "tomek.woj@example.com", "+48555000111", null, CustomerTrustLevel.Watch, now.AddMonths(-3));
        var c7 = NewCustomer(tenant.Id, "Magdalena Dąbrowska", "m.dabrowska@example.com", "+48555777666", null, CustomerTrustLevel.Good, now.AddYears(-1));
        db.Customers.AddRange(c1, c2, c3, c4, c5, c6, c7);

        // 6. WYNAJMY — historia + aktywne
        // Wynajem 1: zakończony tydzień temu, opłacony, ze zwrotem + opinia
        var r1 = NewRental(tenant.Id, c1.Id, now.AddDays(-10), now.AddDays(-7), RentalStatus.Completed, "DepositPaid",
            now.AddDays(-10), now.AddDays(-7), 240m);
        var r1i = new[]
        {
            NewRentalItem(r1.Id, products[0].Id, 1, 80m, 240m)
        };
        var r1rev = new RentalReview
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            RentalId = r1.Id,
            CustomerId = c1.Id,
            QualityScore = 5,
            PriceScore = 5,
            ServiceScore = 5,
            Comment = "Super rower, w idealnym stanie. Polecam!",
            CreatedAtUtc = now.AddDays(-6)
        };

        // Wynajem 2: aktywny — wydany, jeszcze nie zwrócony
        var r2 = NewRental(tenant.Id, c2.Id, now.AddDays(-2), now.AddDays(2), RentalStatus.Active, "DepositPaid",
            now.AddDays(-2), null, 480m);
        var r2i = new[]
        {
            NewRentalItem(r2.Id, products[3].Id, 1, 120m, 480m)
        };

        // Wynajem 3: zarezerwowany na jutro — opłacony, jeszcze nie wydany
        var r3 = NewRental(tenant.Id, c3.Id, now.AddDays(1), now.AddDays(3), RentalStatus.Confirmed, "DepositPaid",
            null, null, 360m);
        var r3i = new[]
        {
            NewRentalItem(r3.Id, products[6].Id, 1, 130m, 260m),
            NewRentalItem(r3.Id, products[8].Id, 1, 50m, 100m)
        };

        // Wynajem 4: nowa rezerwacja, niezaopłacony (alert!)
        // PaymentStatus jest NOT NULL — używamy "" zamiast null. RentalGuards.IsRentalPaid() i tak filtruje po whitelist.
        var r4 = NewRental(tenant.Id, c5.Id, now.AddDays(3), now.AddDays(5), RentalStatus.Pending, "",
            null, null, 180m);
        var r4i = new[]
        {
            NewRentalItem(r4.Id, products[5].Id, 1, 90m, 180m)
        };

        // Wynajem 5: zakończony 2 tygodnie temu
        var r5 = NewRental(tenant.Id, c7.Id, now.AddDays(-21), now.AddDays(-14), RentalStatus.Completed, "DepositPaid",
            now.AddDays(-21), now.AddDays(-14), 770m);
        var r5i = new[]
        {
            NewRentalItem(r5.Id, products[6].Id, 1, 130m, 910m), // 7 dni × 130
            NewRentalItem(r5.Id, products[8].Id, 1, 50m, 350m)
        };
        var r5rev = new RentalReview
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            RentalId = r5.Id,
            CustomerId = c7.Id,
            QualityScore = 4,
            PriceScore = 5,
            ServiceScore = 5,
            Comment = "Dobra obsługa, sprzęt sprawny. Drobne rysy na nartach.",
            CreatedAtUtc = now.AddDays(-13)
        };

        db.Rentals.AddRange(r1, r2, r3, r4, r5);
        db.RentalItems.AddRange(r1i.Concat(r2i).Concat(r3i).Concat(r4i).Concat(r5i));
        db.Set<RentalReview>().AddRange(r1rev, r5rev);

        // Aktualizuj HowManyRented dla aktywnego wynajmu r2 (1 szt. kajaka wydana)
        products[3].HowManyRented = 1;

        // 7. COMPANY INFO — żeby formularze nie były puste
        db.CompanyInfos.Add(new CompanyInfo
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Name = tenant.Name,
            Address = "Rynek Główny 15, 31-008 Kraków",
            City = "Kraków",
            Voivodeship = "małopolskie",
            PhoneNumber = "+48 12 555 0123",
            Email = $"kontakt+{shortId}@rentspot.demo",
            NIP = "5252316345",
            REGON = "12345678901234",
            LegalForm = "Działalność gospodarcza",
            OpeningHours = "Pn-Pt 9:00-19:00, Sob 10:00-16:00, Niedz zamknięte"
        });

        await db.SaveChangesAsync(ct);
        sw.Stop();

        _logger.LogInformation(
            "Created fresh demo tenant {TenantId} for {Email} — 12 products, 7 customers, 5 rentals in {Ms}ms",
            tenant.Id, userEmail, sw.ElapsedMilliseconds);

        return user;
    }

    private static Product P(Guid tenantId, string name, string sku, string category, decimal price, int qty, string producer, string model, string description) =>
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
            Description = description,
            CreatedAtUtc = DateTime.UtcNow
        };

    private static Customer NewCustomer(Guid tenantId, string fullName, string email, string phone, string? doc, CustomerTrustLevel trust, DateTime createdAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            FullName = fullName,
            Email = email,
            PhoneNumber = phone,
            DocumentNumber = doc,
            TrustLevel = trust,
            CreatedAtUtc = createdAt
        };

    private static Rental NewRental(Guid tenantId, Guid customerId, DateTime startUtc, DateTime endUtc,
        RentalStatus status, string paymentStatus, DateTime? issuedAt, DateTime? returnedAt, decimal total) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CustomerId = customerId,
            StartDateUtc = startUtc,
            EndDateUtc = endUtc,
            Status = status,
            PaymentStatus = paymentStatus,
            IssuedAtUtc = issuedAt,
            ReturnedAtUtc = returnedAt,
            TotalAmount = total,
            CreatedAtUtc = startUtc.AddDays(-1),
            RentalType = RentalType.Daily,
            Source = RentalSource.InStore
        };

    private static RentalItem NewRentalItem(Guid rentalId, Guid productId, int qty, decimal pricePerDay, decimal subtotal) =>
        new()
        {
            Id = Guid.NewGuid(),
            RentalId = rentalId,
            ProductId = productId,
            Quantity = qty,
            PricePerDay = pricePerDay,
            Subtotal = subtotal
        };
}
