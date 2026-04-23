using System.Reflection;
using FluentAssertions;
using SportRental.Admin.Services.Email;

namespace SportRental.Admin.Tests.Services.Email;

/// <summary>
/// Regresja dla commitu a1afeed (fix mojibake + fleksja). Klient zgłosił maile
/// "DzieĹ„ dobry" + "za 2 godzin". Te testy pilnują, żeby helpery w
/// RentalReminderService nie wróciły do starego zachowania.
/// </summary>
public class RentalReminderEncodingTests
{
    private static string InvokeFormatRemaining(TimeSpan remaining)
    {
        var method = typeof(RentalReminderService)
            .GetMethod("FormatRemaining", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull("FormatRemaining should exist as private static helper");
        return (string)method!.Invoke(null, new object[] { remaining })!;
    }

    [Theory]
    [InlineData(60, "za 1 godzinę")]          // mianownik + biernik l. poj.
    [InlineData(120, "za 2 godziny")]         // few (2-4)
    [InlineData(180, "za 3 godziny")]         // few
    [InlineData(240, "za 4 godziny")]         // few
    [InlineData(300, "za 5 godzin")]          // many (5-21)
    [InlineData(12 * 60, "za 12 godzin")]     // many (mod100 in 10..19)
    [InlineData(22 * 60, "za 22 godziny")]    // few (mod100 poza 10..19)
    [InlineData(24 * 60, "za 24 godziny")]    // few
    public void FormatRemaining_ForHours_UsesPolishPlural(int totalMinutes, string expected)
    {
        var result = InvokeFormatRemaining(TimeSpan.FromMinutes(totalMinutes));
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(1, "za 1 minutę")]
    [InlineData(2, "za 2 minuty")]
    [InlineData(5, "za 5 minut")]
    [InlineData(15, "za 15 minut")]
    [InlineData(22, "za 22 minuty")]
    public void FormatRemaining_ForMinutes_UsesPolishPlural(int minutes, string expected)
    {
        var result = InvokeFormatRemaining(TimeSpan.FromMinutes(minutes));
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-120)]
    public void FormatRemaining_WhenNonPositive_ReturnsLadaChwila(int minutes)
    {
        var result = InvokeFormatRemaining(TimeSpan.FromMinutes(minutes));
        result.Should().Be("lada chwila");
    }

    [Fact]
    public void FormatRemaining_DoesNotReturnLegacyPluralForTwoHours()
    {
        // Stary kod w SendReminderEmail używał `{hoursUntilEnd:F0} godzin`,
        // co dawało "za 2 godzin" — dokładnie to co zgłosił klient. Zabezpieczenie.
        var result = InvokeFormatRemaining(TimeSpan.FromHours(2));
        result.Should().NotBe("za 2 godzin");
        result.Should().Be("za 2 godziny");
    }

    [Fact]
    public void FormatRemaining_OutputHasNoMojibakeBytes()
    {
        // Gdyby plik RentalReminderService.cs był znów zapisany w Windows-1252,
        // "godzinę" stałoby się "godzinÄ™". Sprawdź charakterystyczne sekwencje.
        foreach (var minutes in new[] { 1, 2, 5, 60, 120, 300 })
        {
            var result = InvokeFormatRemaining(TimeSpan.FromMinutes(minutes));
            result.Should().NotContain("Ä");
            result.Should().NotContain("Ĺ");
            result.Should().NotContain("Ăł");
        }
    }
}
