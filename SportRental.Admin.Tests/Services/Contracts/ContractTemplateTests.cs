using System.Reflection;
using FluentAssertions;
using SportRental.Admin.Services.Contracts;
using SportRental.Infrastructure.Domain;

namespace SportRental.Admin.Tests.Services.Contracts;

public class ContractTemplateTests
{
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
}
