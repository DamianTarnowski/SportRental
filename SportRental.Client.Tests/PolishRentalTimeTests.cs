using FluentAssertions;
using SportRental.Shared.Time;

namespace SportRental.Client.Tests;

public class PolishRentalTimeTests
{
    [Theory]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Utc)]
    public void ToUtc_TreatsDatetimeLocalFieldsAsWarsawTime_RegardlessOfKind(DateTimeKind kind)
    {
        var selectedWallClock = new DateTime(2026, 8, 1, 12, 0, 0, kind);

        var utc = PolishRentalTime.ToUtc(selectedWallClock);

        utc.Should().Be(new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void ToUtc_UsesWinterOffset()
    {
        var selectedWallClock = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Unspecified);

        PolishRentalTime.ToUtc(selectedWallClock)
            .Should().Be(new DateTime(2026, 1, 15, 11, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void TryToUtc_RejectsNonexistentSpringDstHour()
    {
        var selectedWallClock = new DateTime(2026, 3, 29, 2, 30, 0, DateTimeKind.Unspecified);

        PolishRentalTime.TryToUtc(selectedWallClock, out _).Should().BeFalse();
    }

    [Fact]
    public void ToUtc_AmbiguousAutumnHour_UsesLaterStandardTimeOccurrence()
    {
        var selectedWallClock = new DateTime(2026, 10, 25, 2, 30, 0, DateTimeKind.Unspecified);

        PolishRentalTime.ToUtc(selectedWallClock)
            .Should().Be(new DateTime(2026, 10, 25, 1, 30, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void IsStartSafelyInFuture_RequiresFullLeadTime()
    {
        var now = new DateTime(2026, 7, 10, 10, 0, 0, DateTimeKind.Utc);

        PolishRentalTime.IsStartSafelyInFuture(now.AddMinutes(1).AddSeconds(59), now)
            .Should().BeFalse();
        PolishRentalTime.IsStartSafelyInFuture(now.AddMinutes(2), now)
            .Should().BeTrue();
    }

    [Fact]
    public void EarliestStartLocal_RoundsUpToMinuteAfterLeadTime()
    {
        var now = new DateTime(2026, 7, 10, 10, 0, 45, DateTimeKind.Utc);

        PolishRentalTime.EarliestStartLocal(now)
            .Should().Be(new DateTime(2026, 7, 10, 12, 3, 0, DateTimeKind.Unspecified));
    }
}
