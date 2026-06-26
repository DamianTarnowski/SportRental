using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;
using SportRental.Shared.Identity;

namespace SportRental.Admin.Data;

/// Tworzy ŚWIEŻY izolowany tenant demo per session "Wypróbuj demo".
/// Każdy klik → nowy Guid tenant + nowy user demo+<shortId>@rentspot.eu + bogaty seed.
/// Po DemoExpiresAtUtc (default +8h) background cleanup usuwa cały tenant.
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

        // 1. TENANT
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

        // 2. ROLES (idempotent)
        foreach (var role in new[] { RoleNames.SuperAdmin, RoleNames.Owner, RoleNames.Employee, RoleNames.Client })
        {
            if (!await _roleManager.RoleExistsAsync(role))
                await _roleManager.CreateAsync(new IdentityRole<Guid>(role));
        }

        // 3. USER
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

        // 4. PRODUKTY — 48 sztuk z ładnymi zdjęciami z Unsplash CDN
        var products = BuildProducts(tenant.Id);
        db.Products.AddRange(products);

        // 5. KLIENCI — 28 osób z różnymi trust levels
        var customers = BuildCustomers(tenant.Id, now);
        db.Customers.AddRange(customers);

        // 6. WYNAJMY — 20 sztuk w różnych statusach i okresach
        var (rentals, items, reviews) = BuildRentals(tenant.Id, customers, products, now);
        db.Rentals.AddRange(rentals);
        db.RentalItems.AddRange(items);
        db.Set<RentalReview>().AddRange(reviews);

        // 7. COMPANY INFO
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
            "Created fresh demo tenant {TenantId} for {Email} — {Products} products, {Customers} customers, {Rentals} rentals in {Ms}ms",
            tenant.Id, userEmail, products.Length, customers.Length, rentals.Count, sw.ElapsedMilliseconds);

        return user;
    }

    // ==================== PRODUKTY ====================

    private static Product[] BuildProducts(Guid tenantId)
    {
        // Unsplash CDN — dobrze dobrane zdjęcia sportowe, hotlinkowalne, free.
        // Format: photo-<id>?w=800&q=80&auto=format&fit=crop
        const string U = "https://images.unsplash.com/photo-";
        const string Q = "?w=800&q=80&auto=format&fit=crop";

        var templates = new (string Name, string Sku, string Cat, decimal Price, int Qty, string Producer, string Model, string Desc, string PhotoId)[]
        {
            // ===== ROWERY (12) =====
            ("Rower MTB Trek Marlin 6", "BIKE-MTB-001", "Rowery", 80m, 4, "Trek", "Marlin 6", "Lekki rower górski idealny na leśne szlaki. Hamulce hydrauliczne, 21 biegów.", "1485965120184-e220f721d03e"),
            ("Rower trekkingowy Kross Trans 5.0", "BIKE-TRK-002", "Rowery", 60m, 3, "Kross", "Trans 5.0", "Wszechstronny rower z amortyzowanym widelcem. Idealny do miasta i na dłuższe trasy.", "1532298229144-0ec0c57515c7"),
            ("Rower elektryczny Romet e-Wagant", "EBIKE-001", "Rowery", 180m, 2, "Romet", "e-Wagant", "E-bike z silnikiem centralnym, zasięg do 100km. Wspomaganie 4 trybami.", "1571333250631-7e6cb3a4c45c"),
            ("Rower miejski Cannondale Quick", "BIKE-CITY-001", "Rowery", 55m, 5, "Cannondale", "Quick CX 3", "Wygodny rower miejski z prostą sylwetką. 24 biegi Shimano.", "1502744688674-c619d1586c9e"),
            ("Rower szosowy Specialized Allez", "BIKE-RD-001", "Rowery", 140m, 2, "Specialized", "Allez Sport", "Aluminiowa rama, widelec carbon, grupa Shimano 105. Dla zaawansowanych.", "1517649763962-0c623066013b"),
            ("Rower MTB Giant Talon", "BIKE-MTB-002", "Rowery", 75m, 3, "Giant", "Talon 29 2", "Hardtail z kołami 29\", 1×11 napęd. Dla MTB enthusiasts.", "1576435728678-68d0fbf94e91"),
            ("Rower dziecięcy Kross Junior 16\"", "BIKE-KID-001", "Rowery", 35m, 6, "Kross", "Junior 1.0", "Bezpieczny rower dla dzieci 4-7 lat. Boczne kółka w zestawie.", "1545167622-3a6ac756afa4"),
            ("Rower BMX WeThePeople", "BIKE-BMX-001", "Rowery", 65m, 2, "WeThePeople", "Justice", "Solidny rower BMX do skateparku. Aluminiowa rama, hamulec U-brake.", "1559348349-86f1f65817fe"),
            ("Rower gravel Trek Checkpoint", "BIKE-GRV-001", "Rowery", 150m, 2, "Trek", "Checkpoint ALR 5", "Gravel z grupą GRX. Idealny na asfalt i szutry.", "1591047133456-7a8db48ba8f5"),
            ("Rower elektryczny Cube Reaction Hybrid", "EBIKE-002", "Rowery", 200m, 2, "Cube", "Reaction Hybrid Performance 625", "E-MTB z silnikiem Bosch CX, bateria 625Wh, zasięg do 130km.", "1576435728678-68d0fbf94e91"),
            ("Rower turystyczny Romet Wagant 4", "BIKE-TRK-003", "Rowery", 65m, 3, "Romet", "Wagant 4", "Solidny rower turystyczny, bagażnik + błotniki w zestawie.", "1532298229144-0ec0c57515c7"),
            ("Rower fatbike Mongoose Argus", "BIKE-FAT-001", "Rowery", 95m, 2, "Mongoose", "Argus Sport", "Fatbike z kołami 26×4\". Przejedzie wszędzie — śnieg, piasek, błoto.", "1571068316344-75bc76f77890"),

            // ===== KAJAKI (8) =====
            ("Kajak 2-osobowy Pelican Argo 100XR", "KAY-002-001", "Kajaki", 120m, 2, "Pelican", "Argo 100XR", "Stabilny kajak rekreacyjny dla 2 osób. RAM-X, wiosła i kamizelki w zestawie.", "1532274402911-5a369e4c4bb5"),
            ("Kajak turystyczny Wave Tour 280", "KAY-001-001", "Kajaki", 80m, 5, "Wave Sport", "Tour 280", "Lekki kajak rekreacyjny na jeziora i spokojne rzeki.", "1530866495561-a52b6a72f7c1"),
            ("Kajak morski Prijon Touryak 470", "KAY-SEA-001", "Kajaki", 160m, 2, "Prijon", "Touryak 470", "Profesjonalny kajak morski. Hatch wodoszczelny, ster.", "1530866495561-a52b6a72f7c1"),
            ("Kajak pompowany Aqua Marina Memba", "KAY-INF-001", "Kajaki", 110m, 3, "Aqua Marina", "Memba 330", "Pompowany kajak 1-os z plecakiem. Mieści się w bagażniku.", "1532274402911-5a369e4c4bb5"),
            ("Kajak składany Oru Kayak Beach LT", "KAY-FOLD-001", "Kajaki", 140m, 2, "Oru Kayak", "Beach LT", "Składany kajak origami z polipropylenu. Składa się w plecak.", "1502136969935-8d8eef54d77b"),
            ("Kajak 3-os Pelican Maxim", "KAY-003-001", "Kajaki", 150m, 2, "Pelican", "Maxim 100X", "Kajak rodzinny dla 3 osób + miejsce na dziecko.", "1532274402911-5a369e4c4bb5"),
            ("Kajak whitewater Dagger Jitsu", "KAY-WW-001", "Kajaki", 130m, 2, "Dagger", "Jitsu 6.0", "Kajak na bystrza klasy III-IV. Dla zaawansowanych.", "1502136969935-8d8eef54d77b"),
            ("Kajak rybacki Pelican Catch 100", "KAY-FISH-001", "Kajaki", 135m, 2, "Pelican", "Catch 100", "Kajak rybacki z uchwytami na wędki + miejsce na sprzęt.", "1530866495561-a52b6a72f7c1"),

            // ===== SUP (6) =====
            ("Deska SUP Aqua Marina Beast 10'6", "SUP-001", "SUP", 90m, 3, "Aqua Marina", "Beast 10'6", "Pompowany SUP all-around. Idealny dla początkujących.", "1502780402662-acc01917cd1f"),
            ("SUP Red Paddle Co Ride 10'6", "SUP-002", "SUP", 130m, 3, "Red Paddle Co", "Ride 10'6", "Premium SUP all-round. Najwyższa jakość, wiosło carbon w zestawie.", "1502780402662-acc01917cd1f"),
            ("SUP Aqua Marina Drift Fishing", "SUP-003", "SUP", 110m, 2, "Aqua Marina", "Drift", "SUP z uchwytami na wędki. Stabilny do rybołówstwa.", "1502780402662-acc01917cd1f"),
            ("SUP touring Starboard Sport Touring", "SUP-004", "SUP", 140m, 2, "Starboard", "Sport Touring 12'6", "SUP touring na dłuższe wycieczki. Szybki na płaskiej wodzie.", "1502780402662-acc01917cd1f"),
            ("SUP dziecięcy Aqua Marina Vapor 9'10", "SUP-KID-001", "SUP", 65m, 4, "Aqua Marina", "Vapor 9'10", "Mniejszy SUP idealny dla dzieci 8-14 lat.", "1502780402662-acc01917cd1f"),
            ("SUP race Naish One ONE 12'6", "SUP-RACE-001", "SUP", 160m, 2, "Naish", "ONE 12'6", "SUP wyścigowy. Lekki, szybki, dla zaawansowanych.", "1502780402662-acc01917cd1f"),

            // ===== NARTY (8) =====
            ("Narty all-mountain Atomic Vantage 90", "SKI-AM-001", "Narty", 130m, 6, "Atomic", "Vantage 90 Ti", "Wszechstronne narty na cały stok. 168-184cm.", "1551524559-8af4e6624178"),
            ("Narty freeride Salomon QST 99", "SKI-FR-001", "Narty", 160m, 3, "Salomon", "QST 99", "Narty freeride na puch. Szerokie pod butem, lekkie.", "1551524559-8af4e6624178"),
            ("Narty carving Head Supershape e-Speed", "SKI-CV-001", "Narty", 145m, 4, "Head", "Supershape e-Speed", "Narty carvingowe na ratrak. Stabilne na dużych prędkościach.", "1551524559-8af4e6624178"),
            ("Narty turowe Movement Race Pro 76", "SKI-TR-001", "Narty", 170m, 2, "Movement", "Race Pro 76", "Lekkie narty turowe ze skiną. Wiązanie pin-tech.", "1551524559-8af4e6624178"),
            ("Narty dziecięce Rossignol Experience Pro Kid", "SKI-KID-001", "Narty", 60m, 8, "Rossignol", "Experience Pro Kid", "Narty dla dzieci 6-12 lat. Długości 100-130cm.", "1551524559-8af4e6624178"),
            ("Narty park Armada Edollo", "SKI-PARK-001", "Narty", 150m, 2, "Armada", "Edollo", "Park & pipe narty. Symetryczne, twin-tip, dla freestyle.", "1551524559-8af4e6624178"),
            ("Narty backcountry Black Crows Camox", "SKI-BC-001", "Narty", 175m, 2, "Black Crows", "Camox", "Narty na poza-trasowe. Dobry kompromis między pylem a stokiem.", "1551524559-8af4e6624178"),
            ("Narty slalom Volkl Racetiger SL", "SKI-SL-001", "Narty", 155m, 2, "Volkl", "Racetiger SL", "Narty slalomowe wyścigowe. Krótkie, agresywne.", "1551524559-8af4e6624178"),

            // ===== SNOWBOARD (4) =====
            ("Snowboard Burton Custom 156", "SNB-001", "Snowboard", 110m, 4, "Burton", "Custom 156", "All-mountain snowboard. Directional twin, dla zaawansowanych.", "1551524559-8af4e6624178"),
            ("Snowboard freestyle Bataleon Evil Twin", "SNB-002", "Snowboard", 115m, 3, "Bataleon", "Evil Twin", "Park & all-mountain. True twin shape, miękki flex.", "1551524559-8af4e6624178"),
            ("Snowboard powder Jones Hovercraft", "SNB-003", "Snowboard", 130m, 2, "Jones", "Hovercraft", "Powder board z taperem. Idealny na głęboki puch.", "1551524559-8af4e6624178"),
            ("Snowboard dziecięcy Burton Riglet", "SNB-KID-001", "Snowboard", 50m, 5, "Burton", "Riglet 90", "Snowboard dla dzieci 4-7 lat z uchwytem dla rodzica.", "1551524559-8af4e6624178"),

            // ===== BUTY (4) =====
            ("Buty narciarskie Tecnica Mach1 MV", "BOOT-SKI-001", "Buty", 50m, 8, "Tecnica", "Mach1 MV", "All-mountain buty. Flex 110, ostatnia szerokość 100mm.", "1605648916361-9bc12ad6a569"),
            ("Buty narciarskie Lange XT3 Free 130", "BOOT-SKI-002", "Buty", 55m, 6, "Lange", "XT3 Free 130", "Buty hybridowe — zjazd + ski-tour. Walk mode.", "1605648916361-9bc12ad6a569"),
            ("Buty snowboardowe ThirtyTwo TM-2", "BOOT-SNB-001", "Buty", 45m, 6, "ThirtyTwo", "TM-2", "Średnio-twardy boot. Dla freestyle i all-mountain.", "1605648916361-9bc12ad6a569"),
            ("Buty snowboardowe Burton Photon BOA", "BOOT-SNB-002", "Buty", 50m, 5, "Burton", "Photon BOA", "Buty z systemem BOA Coiler. Szybkie zakładanie.", "1605648916361-9bc12ad6a569"),

            // ===== KASKI (3) =====
            ("Kask narciarski Salomon Driver", "HELM-SKI-001", "Kaski", 20m, 12, "Salomon", "Driver", "Lekki kask z systemem MIPS. Certyfikat EN 1077.", "1599481238445-9c2e90e7d3a6"),
            ("Kask rowerowy Giro Tyrant", "HELM-BIKE-001", "Kaski", 15m, 15, "Giro", "Tyrant MIPS", "Full-face kask MTB/BMX. MIPS, removable chinbar.", "1599481238445-9c2e90e7d3a6"),
            ("Kask snowboardowy Smith Variant", "HELM-SNB-001", "Kaski", 22m, 8, "Smith", "Variant Brim MIPS", "Kask z daszkiem. Świetna wentylacja, MIPS.", "1599481238445-9c2e90e7d3a6"),

            // ===== AKCESORIA (3) =====
            ("Kijki trekkingowe Black Diamond Trail Pro", "POLE-TRK-001", "Akcesoria", 15m, 20, "Black Diamond", "Trail Pro", "Aluminiowe kijki z FlickLock. Składane 100-140cm.", "1551632811-561732d1e306"),
            ("Plecak rowerowy Deuter Race EXP Air 14+3", "BAG-BIKE-001", "Akcesoria", 25m, 10, "Deuter", "Race EXP Air", "Plecak z systemem Aircomfort. Mocowanie na kask + light.", "1551632811-561732d1e306"),
            ("Wiosło SUP carbon Aqua Marina Solid", "PADDLE-SUP-001", "Akcesoria", 18m, 8, "Aqua Marina", "Carbon Solid", "Wiosło SUP z carbonu, regulacja 170-220cm. Lekkie 750g.", "1551632811-561732d1e306"),
        };

        return templates.Select(t => new Product
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = t.Name,
            Sku = t.Sku,
            Category = t.Cat,
            DailyPrice = t.Price,
            AvailableQuantity = t.Qty,
            Producer = t.Producer,
            Model = t.Model,
            Description = t.Desc,
            ImageUrl = $"{U}{t.PhotoId}{Q}",
            Available = true,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-Random.Shared.Next(7, 180))
        }).ToArray();
    }

    // ==================== KLIENCI ====================

    private static Customer[] BuildCustomers(Guid tenantId, DateTime now)
    {
        var rng = new Random(42); // stable per call
        var firsts = new[] { "Anna", "Jan", "Maria", "Piotr", "Katarzyna", "Tomasz", "Magdalena", "Krzysztof", "Agnieszka", "Marcin", "Joanna", "Łukasz", "Ewa", "Adam", "Monika", "Paweł", "Aleksandra", "Michał", "Karolina", "Grzegorz", "Natalia", "Mateusz", "Justyna", "Jakub", "Beata", "Marek", "Iwona", "Andrzej" };
        var lasts = new[] { "Nowak", "Kowalski", "Wiśniewski", "Lewandowski", "Zieliński", "Wojciechowski", "Dąbrowski", "Kamiński", "Mazur", "Krawczyk", "Piotrowski", "Grabowski", "Pawlak", "Michalski", "Król", "Wieczorek", "Wróbel", "Kaczmarek", "Zając", "Jabłoński", "Wójcik", "Adamczyk", "Stępień", "Sikorski", "Bąk", "Jankowski", "Witkowski", "Walczak" };

        var trustOpts = new[] { CustomerTrustLevel.Good, CustomerTrustLevel.Good, CustomerTrustLevel.Good, CustomerTrustLevel.Unverified, CustomerTrustLevel.Unverified, CustomerTrustLevel.Watch }; // bias toward Good

        return Enumerable.Range(0, 28).Select(i =>
        {
            var first = firsts[i % firsts.Length];
            var last = lasts[(i * 7) % lasts.Length];
            var emailLocal = $"{first.ToLowerInvariant()}.{last.ToLowerInvariant().Replace("ą","a").Replace("ć","c").Replace("ę","e").Replace("ł","l").Replace("ń","n").Replace("ó","o").Replace("ś","s").Replace("ź","z").Replace("ż","z")}";
            return new Customer
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                FullName = $"{first} {last}",
                Email = $"{emailLocal}{i}@example.com",
                PhoneNumber = $"+48{500 + rng.Next(99)}{rng.Next(100, 1000)}{rng.Next(100, 1000)}",
                DocumentNumber = i % 4 == 0 ? $"{80 + rng.Next(20):00}{rng.Next(1, 13):00}{rng.Next(1, 28):00}{rng.Next(10000, 99999):00000}" : null,
                TrustLevel = trustOpts[i % trustOpts.Length],
                CreatedAtUtc = now.AddDays(-rng.Next(5, 540))
            };
        }).ToArray();
    }

    // ==================== WYNAJMY ====================

    private static (List<Rental> Rentals, List<RentalItem> Items, List<RentalReview> Reviews) BuildRentals(
        Guid tenantId, Customer[] customers, Product[] products, DateTime now)
    {
        var rentals = new List<Rental>();
        var items = new List<RentalItem>();
        var reviews = new List<RentalReview>();
        var rng = new Random(123);

        // 8 zakończonych z historią (różne czasy zwrotu, część z opiniami)
        for (int i = 0; i < 8; i++)
        {
            var startOffset = -rng.Next(7, 120);
            var duration = rng.Next(2, 8);
            var customer = customers[rng.Next(customers.Length)];
            var product = products[rng.Next(products.Length)];
            var startUtc = now.AddDays(startOffset);
            var endUtc = startUtc.AddDays(duration);
            var rental = NewRental(tenantId, customer.Id, startUtc, endUtc, RentalStatus.Completed, "DepositPaid",
                startUtc, endUtc, product.DailyPrice * duration);
            rentals.Add(rental);
            items.Add(NewItem(rental.Id, product.Id, 1, product.DailyPrice, product.DailyPrice * duration));

            if (i % 2 == 0) // co druga ma opinię
            {
                reviews.Add(new RentalReview
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    RentalId = rental.Id,
                    CustomerId = customer.Id,
                    QualityScore = rng.Next(4, 6),
                    PriceScore = rng.Next(4, 6),
                    ServiceScore = rng.Next(4, 6),
                    Comment = i switch
                    {
                        0 => "Super sprzęt, polecam! Wszystko jak w opisie.",
                        2 => "Bardzo profesjonalna obsługa, sprzęt sprawny. Drobne rysy nie przeszkadzały.",
                        4 => "Wynająłem na weekend - rewelacja. Już planuję następną wycieczkę.",
                        6 => "Cena rozsądna, sprzęt nowoczesny. Polecam wszystkim.",
                        _ => "Polecam!"
                    },
                    CreatedAtUtc = endUtc.AddDays(1)
                });
            }
        }

        // 4 aktywne (wydane, jeszcze nie zwrócone)
        for (int i = 0; i < 4; i++)
        {
            var customer = customers[(i + 5) % customers.Length];
            var product = products[(i * 4) % products.Length];
            var startUtc = now.AddDays(-rng.Next(1, 5));
            var endUtc = now.AddDays(rng.Next(1, 4));
            var duration = (int)Math.Ceiling((endUtc - startUtc).TotalDays);
            var rental = NewRental(tenantId, customer.Id, startUtc, endUtc, RentalStatus.Active, "DepositPaid",
                startUtc, null, product.DailyPrice * duration);
            rentals.Add(rental);
            items.Add(NewItem(rental.Id, product.Id, 1, product.DailyPrice, product.DailyPrice * duration));

            // aktualizuj HowManyRented produktu — sprzęt jest wydany
            product.HowManyRented += 1;
        }

        // 5 confirmed (opłaconych, jeszcze nie wydanych — przyszłe rezerwacje)
        for (int i = 0; i < 5; i++)
        {
            var customer = customers[(i * 3 + 1) % customers.Length];
            var product = products[(i * 5 + 2) % products.Length];
            var startUtc = now.AddDays(rng.Next(1, 14));
            var duration = rng.Next(2, 6);
            var endUtc = startUtc.AddDays(duration);
            var rental = NewRental(tenantId, customer.Id, startUtc, endUtc, RentalStatus.Confirmed, "DepositPaid",
                null, null, product.DailyPrice * duration);
            rentals.Add(rental);
            items.Add(NewItem(rental.Id, product.Id, 1, product.DailyPrice, product.DailyPrice * duration));
        }

        // 3 pending (rezerwacje bez płatności — alerty!)
        for (int i = 0; i < 3; i++)
        {
            var customer = customers[(i * 2 + 7) % customers.Length];
            var product = products[(i * 6 + 3) % products.Length];
            var startUtc = now.AddDays(rng.Next(2, 10));
            var duration = rng.Next(2, 4);
            var endUtc = startUtc.AddDays(duration);
            var rental = NewRental(tenantId, customer.Id, startUtc, endUtc, RentalStatus.Pending, "",
                null, null, product.DailyPrice * duration);
            rentals.Add(rental);
            items.Add(NewItem(rental.Id, product.Id, 1, product.DailyPrice, product.DailyPrice * duration));
        }

        return (rentals, items, reviews);
    }

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
            // Dla wynajmów opłaconych w seedzie ustawiamy PaidAtUtc tak żeby Dashboard tile
            // "Przychód miesiąca" + Payments KPI miały realne dane od razu.
            PaidAtUtc = paymentStatus is "DepositPaid" or "succeeded" or "paid" or "Paid"
                ? (issuedAt ?? startUtc)
                : (DateTime?)null,
            PaymentMethod = paymentStatus is "DepositPaid" or "succeeded" or "paid" or "Paid"
                ? "Cash"
                : null,
            IssuedAtUtc = issuedAt,
            ReturnedAtUtc = returnedAt,
            TotalAmount = total,
            CreatedAtUtc = startUtc.AddDays(-1),
            RentalType = RentalType.Daily,
            Source = RentalSource.InStore
        };

    private static RentalItem NewItem(Guid rentalId, Guid productId, int qty, decimal pricePerDay, decimal subtotal) =>
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
