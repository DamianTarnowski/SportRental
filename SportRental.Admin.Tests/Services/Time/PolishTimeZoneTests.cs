using FluentAssertions;
using SportRental.Admin.Services.Time;

namespace SportRental.Admin.Tests.Services.Time;

/// <summary>
/// Inwestor zgłosił SMS z godziną końca 10:00 dostarczony o 11:47 — server UTC wypycha
/// `ToLocalTime()` które na Azure/Linux jest no-opem. PolishTimeZone zawsze mapuje
/// na czas warszawski. Te testy pilnują DST i tego, że UTC input jest konwertowany
/// nawet gdy Kind=Unspecified (EF Core zwraca czasem takie DateTime).
/// </summary>
public class PolishTimeZoneTests
{
    [Fact]
    public void FromUtc_InApril_AddsTwoHours_CEST()
    {
        // 2026-04-22 10:00 UTC → 12:00 CEST
        var utc = new DateTime(2026, 4, 22, 10, 0, 0, DateTimeKind.Utc);
        var local = PolishTimeZone.FromUtc(utc);
        local.Hour.Should().Be(12);
        local.Minute.Should().Be(0);
        local.Day.Should().Be(22);
    }

    [Fact]
    public void FromUtc_InJanuary_AddsOneHour_CET()
    {
        // 2026-01-15 10:00 UTC → 11:00 CET
        var utc = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        var local = PolishTimeZone.FromUtc(utc);
        local.Hour.Should().Be(11);
        local.Minute.Should().Be(0);
    }

    [Fact]
    public void FromUtc_WhenKindUnspecified_TreatsAsUtc()
    {
        // EF Core PostgreSQL czasem zwraca DateTime z Kind=Unspecified.
        var utcAsUnspecified = new DateTime(2026, 4, 22, 10, 0, 0, DateTimeKind.Unspecified);
        var local = PolishTimeZone.FromUtc(utcAsUnspecified);
        local.Hour.Should().Be(12, "Unspecified powinno być traktowane jak UTC");
    }

    [Fact]
    public void FromUtc_WhenKindLocal_NormalizesToUtcFirst()
    {
        // Gdyby ktoś przez pomyłkę wrzucił Local, nie wywala się — najpierw normalizuje.
        var local = new DateTime(2026, 4, 22, 10, 0, 0, DateTimeKind.Local);
        var result = PolishTimeZone.FromUtc(local);
        // Nie sprawdzamy wartości (zależy od serwera), tylko że nie rzuciło.
        result.Should().NotBe(default);
    }

    [Fact]
    public void FromUtc_AtDstTransition_Spring2026_HandlesGap()
    {
        // DST w Polsce: ostatnia niedziela marca, switch o 01:00 UTC (CET→CEST).
        // 2026-03-29 00:30 UTC jest jeszcze przed switchem → UTC+1 = 01:30 CET.
        var utcBeforeSwitch = new DateTime(2026, 3, 29, 0, 30, 0, DateTimeKind.Utc);
        var localBefore = PolishTimeZone.FromUtc(utcBeforeSwitch);
        localBefore.Hour.Should().Be(1, "przed switchem CET jest UTC+1, więc 00:30 UTC = 01:30 CET");

        // 2026-03-29 01:30 UTC jest już po switchu → UTC+2 = 03:30 CEST.
        var utcAfterSwitch = new DateTime(2026, 3, 29, 1, 30, 0, DateTimeKind.Utc);
        var localAfter = PolishTimeZone.FromUtc(utcAfterSwitch);
        localAfter.Hour.Should().Be(3, "po switchu CEST jest UTC+2, więc 01:30 UTC = 03:30 CEST");
    }

    [Fact]
    public void Instance_IsInitializedOnce()
    {
        var a = PolishTimeZone.Instance;
        var b = PolishTimeZone.Instance;
        a.Should().BeSameAs(b);
    }
}
