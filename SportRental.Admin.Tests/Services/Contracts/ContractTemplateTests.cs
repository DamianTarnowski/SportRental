using System.Reflection;
using FluentAssertions;
using SportRental.Admin.Services.Contracts;
using SportRental.Infrastructure.Domain;

namespace SportRental.Admin.Tests.Services.Contracts;

public class ContractTemplateTests
{
    [Fact]
    public void ParseContractTerms_ExtractsLegacyConditionsAndDropsTextGraphics()
    {
        const string legacyTemplate = """
            ═══════════════════════════════════════
            UMOWA WYPOŻYCZENIA SPRZĘTU SPORTOWEGO
            ───────────────────────────────────────
            WARUNKI UMOWY
            ───────────────────────────────────────
            1. Zwrot sprzętu w stanie niepogorszonym.
            2. Odpowiedzialność za uszkodzenie lub zagubienie sprzętu,
               także gdy opis zajmuje kolejny wiersz.
            ───────────────────────────────────────
            PODPISY
            Życzymy udanego wypoczynku! 🎿⛷️🏂
            """;

        var method = typeof(QuestPdfContractGenerator)
            .GetMethod("ParseContractTerms", BindingFlags.Static | BindingFlags.NonPublic);
        var result = method!.Invoke(null, new object[] { legacyTemplate }) as IReadOnlyList<string>;

        result.Should().Equal(
            "Zwrot sprzętu w stanie niepogorszonym.",
            "Odpowiedzialność za uszkodzenie lub zagubienie sprzętu, także gdy opis zajmuje kolejny wiersz.");
        string.Join(" ", result!).Should().NotContain("─").And.NotContain("🎿");
    }

    [Fact]
    public void ResolveTermsContent_LegacyFullDocumentWithoutTermsSection_UsesStandardTerms()
    {
        const string template = """
            UMOWA WYPOŻYCZENIA
            Firma: {{CompanyName}}
            Klient: {{CustomerName}}
            Okres: {{StartDate}} - {{EndDate}}
            {{ItemsTable}}
            Razem: {{Total}}
            """;
        const string filled = "UMOWA WYPOŻYCZENIA\nFirma: Test\nKlient: Jan\nRazem: 100";
        var method = typeof(QuestPdfContractGenerator)
            .GetMethod("ResolveTermsContent", BindingFlags.Static | BindingFlags.NonPublic);

        var result = method!.Invoke(null, new object[] { template, filled }) as string;

        result.Should().Be(ContractTemplateDefaults.StandardTerms);
    }

    [Fact]
    public void ResolveTermsContent_TermsOnlyTemplate_PreservesCustomizedTerms()
    {
        const string template = """
            1. Sprzęt należy zwrócić czysty.
            2. Przedłużenie wymaga potwierdzenia przez {{CompanyName}}.
            """;
        const string filled = """
            1. Sprzęt należy zwrócić czysty.
            2. Przedłużenie wymaga potwierdzenia przez NorthPeak.
            """;
        var method = typeof(QuestPdfContractGenerator)
            .GetMethod("ResolveTermsContent", BindingFlags.Static | BindingFlags.NonPublic);

        var result = method!.Invoke(null, new object[] { template, filled }) as string;

        result.Should().Be(filled);
    }

    [Theory]
    [InlineData("/images/tenants/11111111-1111-1111-1111-111111111111/logo.png", "images/tenants/11111111-1111-1111-1111-111111111111/logo.png")]
    [InlineData("https://cdn.example.test/images/images/tenants/11111111-1111-1111-1111-111111111111/logo.webp?x=1", "images/tenants/11111111-1111-1111-1111-111111111111/logo.webp")]
    [InlineData("https://example.test/unrelated/logo.png", null)]
    [InlineData("https://example.test/images/tenants/22222222-2222-2222-2222-222222222222/logo.png", null)]
    public void ExtractLogoStoragePath_AcceptsOnlyTenantLogoPaths(string input, string? expected)
    {
        var method = typeof(QuestPdfContractGenerator)
            .GetMethod("ExtractLogoStoragePath", BindingFlags.Static | BindingFlags.NonPublic);

        var result = method!.Invoke(null, new object?[]
        {
            input,
            Guid.Parse("11111111-1111-1111-1111-111111111111")
        }) as string;

        result.Should().Be(expected);
    }

    [Fact]
    public void SanitizePdfText_RemovesUnsupportedBoxDrawingAndEmoji()
    {
        var method = typeof(QuestPdfContractGenerator)
            .GetMethod("SanitizePdfText", BindingFlags.Static | BindingFlags.NonPublic);

        var result = method!.Invoke(null, new object?[] { "Zażółć ─ sprzęt 🎿" }) as string;

        result.Should().Be("Zażółć  sprzęt ");
    }

    [Theory]
    [InlineData("https://cdn.example.test/images/tenants/logo.png", "https://cdn.example.test/images/tenants/logo.png")]
    [InlineData("http://localhost/images/logo.png", "http://localhost/images/logo.png")]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("data:image/png;base64,AAAA", null)]
    [InlineData("/images/tenants/logo.png", null)]
    public void GetEmailLogoUrl_AllowsOnlyAbsoluteHttpResources(string input, string? expected)
    {
        var method = typeof(QuestPdfContractGenerator)
            .GetMethod("GetEmailLogoUrl", BindingFlags.Static | BindingFlags.NonPublic);

        var result = method!.Invoke(null, new object?[] { input }) as string;

        result.Should().Be(expected);
    }

    [Fact]
    public void FillTemplate_ReplacesEveryVariableAdvertisedByDefaultTemplate()
    {
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var rental = new Rental
        {
            Id = Guid.Parse("1954eb65-6f5c-4d0f-a7b0-84a4f8de3dc1"),
            TenantId = tenantId,
            StartDateUtc = new DateTime(2026, 7, 10, 8, 0, 0, DateTimeKind.Utc),
            EndDateUtc = new DateTime(2026, 7, 12, 8, 0, 0, DateTimeKind.Utc),
            TotalAmount = 240,
            DepositAmount = 300
        };
        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            FullName = "Jan Kowalski",
            DocumentNumber = "ABC123456",
            Email = "jan@example.test",
            PhoneNumber = "+48123456789",
            Address = "ul. Klienta 1, Warszawa"
        };
        var company = new CompanyInfo
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Wypożyczalnia Testowa",
            Address = "ul. Sportowa 10",
            PostalCode = "60-001",
            City = "Poznań",
            Voivodeship = "wielkopolskie",
            NIP = "1234567890",
            REGON = "123456789",
            PhoneNumber = "+48999888777",
            Email = "biuro@example.test"
        };
        var product = new Product
        {
            Id = productId,
            TenantId = tenantId,
            Name = "Rower testowy",
            Sku = "TEST-1"
        };
        var item = new RentalItem
        {
            Id = Guid.NewGuid(),
            RentalId = rental.Id,
            ProductId = productId,
            Quantity = 2,
            PricePerDay = 60,
            Subtotal = 240
        };
        const string template = """
            {{RentalId}}|{{CurrentDate}}|{{RentalDays}}
            {{CustomerName}}|{{CustomerDocument}}|{{CustomerEmail}}|{{CustomerPhone}}|{{CustomerAddress}}
            {{StartDate}}|{{EndDate}}|{{ItemsTable}}|{{Total}}|{{Deposit}}
            {{CompanyName}}|{{CompanyAddress}}|{{CompanyPostalCode}}|{{CompanyCity}}|{{CompanyVoivodeship}}
            {{CompanyNIP}}|{{CompanyREGON}}|{{CompanyPhone}}|{{CompanyEmail}}
            """;

        var method = typeof(QuestPdfContractGenerator)
            .GetMethod("FillTemplate", BindingFlags.Static | BindingFlags.NonPublic);
        var result = method!.Invoke(null, new object[]
        {
            template,
            rental,
            new[] { item },
            customer,
            new Dictionary<Guid, Product> { [productId] = product },
            company
        }) as string;

        result.Should().NotBeNull();
        result.Should().NotContain("{{");
        result.Should().Contain("1954EB65");
        result.Should().Contain("ABC123456");
        result.Should().Contain("2");
        result.Should().Contain("Rower testowy x2");
        result.Should().Contain("ul. Sportowa 10, 60-001 Poznań, woj. wielkopolskie");
        result.Should().Contain("1234567890");
        result.Should().Contain("123456789");
        result.Should().Contain("biuro@example.test");
    }

    [Fact]
    public void FillTemplate_DoesNotInterpretTemplateTokensInjectedInCustomerData()
    {
        var rental = new Rental
        {
            Id = Guid.NewGuid(),
            StartDateUtc = new DateTime(2026, 7, 12, 8, 0, 0, DateTimeKind.Utc),
            EndDateUtc = new DateTime(2026, 7, 13, 8, 0, 0, DateTimeKind.Utc)
        };
        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            FullName = "Jan {{CompanyName}}"
        };
        var company = new CompanyInfo { Name = "NorthPeak" };
        var method = typeof(QuestPdfContractGenerator)
            .GetMethod("FillTemplate", BindingFlags.Static | BindingFlags.NonPublic);

        var result = method!.Invoke(null, new object[]
        {
            "{{CustomerName}}|{{CompanyName}}",
            rental,
            Array.Empty<RentalItem>(),
            customer,
            new Dictionary<Guid, Product>(),
            company
        }) as string;

        result.Should().Be("Jan {{CompanyName}}|NorthPeak");
    }
}
