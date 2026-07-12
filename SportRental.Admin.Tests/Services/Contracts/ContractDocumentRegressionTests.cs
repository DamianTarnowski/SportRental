using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using SportRental.Admin.Services.Contracts;
using SportRental.Admin.Services.Email;
using SportRental.Admin.Services.Storage;
using SportRental.Infrastructure.Domain;

namespace SportRental.Admin.Tests.Services.Contracts;

public sealed class ContractDocumentRegressionTests
{
    [Fact]
    public async Task CreateDocumentModel_HourlyRental_UsesHoursAndHourlyRate()
    {
        var fixture = CreateFixture();
        fixture.Rental.RentalType = RentalType.Hourly;
        fixture.Rental.HoursRented = 3;
        fixture.Rental.EndDateUtc = fixture.Rental.StartDateUtc.AddHours(3);
        fixture.Item.PricePerDay = 120m;
        fixture.Item.PricePerHour = 25m;
        fixture.Item.Subtotal = 75m;
        fixture.Rental.TotalAmount = 75m;

        var model = await CreateDocumentModelAsync(fixture);

        model.DurationText.Should().Be("3 godziny");
        model.PriceUnitLabel.Should().Be("CENA / GODZ.");
        model.Items.Should().ContainSingle();
        model.Items[0].UnitPrice.Should().Be(25m);
        model.Items[0].Total.Should().Be(75m);
    }

    [Fact]
    public async Task CreateDocumentModel_DailyRentalAcrossAutumnDst_UsesOneDay()
    {
        var fixture = CreateFixture();
        fixture.Rental.RentalType = RentalType.Daily;
        fixture.Rental.StartDateUtc = new DateTime(2026, 10, 24, 7, 0, 0, DateTimeKind.Utc);
        fixture.Rental.EndDateUtc = new DateTime(2026, 10, 25, 8, 0, 0, DateTimeKind.Utc);
        fixture.Item.Subtotal = fixture.Item.PricePerDay;
        fixture.Rental.TotalAmount = fixture.Item.Subtotal;

        var model = await CreateDocumentModelAsync(fixture);

        (fixture.Rental.EndDateUtc - fixture.Rental.StartDateUtc).TotalHours.Should().Be(25);
        model.DurationText.Should().Be("1 dzień");
        model.Items[0].UnitPrice.Should().Be(fixture.Item.PricePerDay);
    }

    [Fact]
    public async Task CreateDocumentModel_VariableDailyPrice_UsesAverageDerivedFromSubtotal()
    {
        var fixture = CreateFixture();
        fixture.Rental.RentalType = RentalType.Daily;
        fixture.Rental.EndDateUtc = fixture.Rental.StartDateUtc.AddDays(2);
        fixture.Item.Quantity = 2;
        fixture.Item.PricePerDay = 100m;
        fixture.Item.Subtotal = 360m;
        fixture.Rental.TotalAmount = 360m;

        var model = await CreateDocumentModelAsync(fixture);

        model.DurationText.Should().Be("2 dni");
        model.PriceUnitLabel.Should().Be("ŚR. CENA / DZIEŃ");
        model.Items[0].UnitPrice.Should().Be(90m);
        model.Items[0].Total.Should().Be(360m);
    }

    [Fact]
    public async Task CreateDocumentModel_UsesOnlyAcceptedRegulationsSnapshot()
    {
        var fixture = CreateFixture();
        fixture.Company.RegulationsText = "BIEŻĄCY REGULAMIN, KTÓREGO KLIENT NIE AKCEPTOWAŁ";
        fixture.Rental.RegulationsTextSnapshot = "REGULAMIN ZAAKCEPTOWANY PRZY CHECKOUCIE";
        fixture.Rental.RegulationsVersion = "tenant-2026-07-12";
        fixture.Rental.RegulationsHash = "abcdef0123456789";
        fixture.Rental.RegulationsSource = "tenant";

        var acceptedModel = await CreateDocumentModelAsync(fixture);

        acceptedModel.Regulations.Should().NotBeNull();
        acceptedModel.Regulations!.Text.Should().Be("REGULAMIN ZAAKCEPTOWANY PRZY CHECKOUCIE");
        acceptedModel.Regulations.Version.Should().Be("tenant-2026-07-12");
        acceptedModel.Regulations.Hash.Should().Be("abcdef0123456789");
        acceptedModel.Regulations.Source.Should().Be("tenant");
        RentalContractDocument.Generate(acceptedModel).Should().NotBeNullOrEmpty();

        fixture.Rental.RegulationsTextSnapshot = null;
        fixture.Rental.RegulationsVersion = null;
        fixture.Rental.RegulationsHash = null;
        fixture.Rental.RegulationsSource = null;

        var modelWithoutSnapshot = await CreateDocumentModelAsync(fixture);

        modelWithoutSnapshot.Regulations.Should().BeNull(
            "bieżący regulamin firmy nie może zastąpić snapshotu zaakceptowanego przez klienta");
    }

    [Fact]
    public async Task GenerateRentalContract_CorruptedRasterWithValidHeader_FallsBackSafely()
    {
        var fixture = CreateFixture();
        fixture.Company.Tenant!.LogoUrl =
            $"/images/tenants/{fixture.TenantId:D}/{fixture.TenantId:D}.png";
        fixture.Storage
            .Setup(storage => storage.ReadAsync(
                $"images/tenants/{fixture.TenantId:D}/{fixture.TenantId:D}.png",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        var model = await CreateDocumentModelAsync(fixture);
        var pdf = await fixture.Generator.GenerateRentalContractAsync(
            fixture.Rental,
            new[] { fixture.Item },
            fixture.Customer,
            new[] { fixture.Product },
            fixture.Company);

        model.Branding.LogoBytes.Should().BeNull();
        pdf.Should().NotBeNullOrEmpty();
        pdf.Should().StartWith(new byte[] { 0x25, 0x50, 0x44, 0x46 });
    }

    [Fact]
    public async Task CreateDocumentModel_LightBrandColors_UsesReadableTextAndInkFallbacks()
    {
        var fixture = CreateFixture();
        fixture.Company.Tenant!.PrimaryColorHex = "#FFFFFF";
        fixture.Company.Tenant.SecondaryColorHex = "#FFFF00";

        var model = await CreateDocumentModelAsync(fixture);

        model.Branding.PrimaryColor.Should().Be("#FFFFFF");
        model.Branding.SecondaryColor.Should().Be("#FFFF00");
        model.Branding.PrimaryTextColor.Should().Be("#182230");
        model.Branding.PrimaryInkColor.Should().Be("#2F3C7E");
        model.Branding.SecondaryInkColor.Should().Be("#2F3C7E");
    }

    [Theory]
    [InlineData("TenantCustom", "regulamin wypożyczalni")]
    [InlineData("tenant-custom", "regulamin wypożyczalni")]
    [InlineData("PlatformDefault", "standard RentSpot")]
    [InlineData("platform-default", "standard RentSpot")]
    public void FormatRegulationsSource_MapsPersistedSourceValuesToPolishLabels(
        string source,
        string expected)
    {
        var method = typeof(RentalContractDocument).GetMethod(
            "FormatRegulationsSource",
            BindingFlags.Static | BindingFlags.NonPublic);

        method.Should().NotBeNull();
        method!.Invoke(null, new object?[] { source }).Should().Be(expected);
    }

    private static async Task<RentalContractDocumentModel> CreateDocumentModelAsync(TestFixture fixture)
    {
        var method = typeof(QuestPdfContractGenerator).GetMethod(
            "CreateDocumentModelAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var task = method!.Invoke(fixture.Generator, new object?[]
        {
            fixture.Rental,
            new[] { fixture.Item },
            fixture.Customer,
            new[] { fixture.Product },
            fixture.Company,
            ContractTemplateDefaults.StandardTerms,
            CancellationToken.None
        });

        task.Should().BeAssignableTo<Task<RentalContractDocumentModel>>();
        return await (Task<RentalContractDocumentModel>)task!;
    }

    private static TestFixture CreateFixture()
    {
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var storage = new Mock<IFileStorage>(MockBehavior.Strict);
        var generator = new QuestPdfContractGenerator(
            storage.Object,
            Mock.Of<IEmailSender>(),
            Mock.Of<ILogger<QuestPdfContractGenerator>>());
        var rental = new Rental
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CustomerId = customerId,
            StartDateUtc = new DateTime(2026, 7, 12, 8, 0, 0, DateTimeKind.Utc),
            EndDateUtc = new DateTime(2026, 7, 13, 8, 0, 0, DateTimeKind.Utc),
            RentalType = RentalType.Daily,
            TotalAmount = 100m,
            DepositAmount = 30m
        };
        var item = new RentalItem
        {
            Id = Guid.NewGuid(),
            RentalId = rental.Id,
            ProductId = productId,
            Quantity = 1,
            PricePerDay = 100m,
            PricePerHour = 25m,
            Subtotal = 100m
        };
        var product = new Product
        {
            Id = productId,
            TenantId = tenantId,
            Name = "Rower testowy",
            Sku = "ROWER-TEST",
            DailyPrice = 100m,
            HourlyPrice = 25m
        };
        var customer = new Customer
        {
            Id = customerId,
            TenantId = tenantId,
            FullName = "Jan Testowy",
            Email = "jan@example.test",
            PhoneNumber = "+48123456789",
            Address = "ul. Testowa 1"
        };
        var company = new CompanyInfo
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Wypożyczalnia Testowa",
            Address = "ul. Firmowa 1",
            Tenant = new Tenant
            {
                Id = tenantId,
                Name = "Wypożyczalnia Testowa",
                PrimaryColorHex = "#123456",
                SecondaryColorHex = "#E06B65"
            }
        };

        return new TestFixture(
            tenantId,
            storage,
            generator,
            rental,
            item,
            product,
            customer,
            company);
    }

    private sealed record TestFixture(
        Guid TenantId,
        Mock<IFileStorage> Storage,
        QuestPdfContractGenerator Generator,
        Rental Rental,
        RentalItem Item,
        Product Product,
        Customer Customer,
        CompanyInfo Company);
}
