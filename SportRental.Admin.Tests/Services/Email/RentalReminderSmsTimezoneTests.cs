using FluentAssertions;
using SportRental.Admin.Services.Email;
using SportRental.Infrastructure.Domain;

namespace SportRental.Admin.Tests.Services.Email;

/// <summary>
/// Regresja inwestorskiego zgłoszenia: SMS mówił "kończy się 22.04.2026 10:00" a faktyczny
/// koniec wynajmu był 12:00 Warsaw (Azure UTC server + `ToLocalTime` = no-op). Teraz SMS
/// zawsze używa czasu warszawskiego, niezależnie od strefy serwera.
/// </summary>
public class RentalReminderSmsTimezoneTests
{
    [Fact]
    public void BuildReminderSmsText_UsesWarsawTime_NotServerLocal()
    {
        // 2026-04-22 10:00 UTC = 12:00 CEST (Warsaw w kwietniu)
        var rental = new Rental
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            EndDateUtc = new DateTime(2026, 4, 22, 10, 0, 0, DateTimeKind.Utc),
            Customer = new Customer { FullName = "Maciej Czaronek" },
            Items = new List<RentalItem>()
        };

        var company = new CompanyInfo
        {
            TenantId = rental.TenantId,
            Name = "MCB SPORTS",
            PhoneNumber = "+48 500 100 200"
        };

        var products = new Dictionary<Guid, string>();

        var sms = RentalReminderService.BuildReminderSmsText(rental, company, products);

        sms.Should().Contain("12:00", "data w SMS to warszawski czas lokalny, nie UTC");
        sms.Should().NotContain("10:00", "nie wolno wyświetlać czasu UTC jakby był lokalnym");
        sms.Should().Contain("22.04.2026");
        sms.Should().Contain("Maciej Czaronek");
        sms.Should().Contain("MCB SPORTS");
    }

    [Fact]
    public void BuildReminderSmsText_InWinter_UsesCET()
    {
        // 2026-01-15 10:00 UTC = 11:00 CET (Warsaw w styczniu, bez DST)
        var rental = new Rental
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            EndDateUtc = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc),
            Customer = new Customer { FullName = "Anna Kowalska" },
            Items = new List<RentalItem>()
        };

        var company = new CompanyInfo
        {
            TenantId = rental.TenantId,
            Name = "Wypożyczalnia Zima"
        };

        var sms = RentalReminderService.BuildReminderSmsText(rental, company, new Dictionary<Guid, string>());

        sms.Should().Contain("11:00", "zimą Warsaw jest UTC+1 (CET)");
        sms.Should().NotContain("10:00");
    }
}
