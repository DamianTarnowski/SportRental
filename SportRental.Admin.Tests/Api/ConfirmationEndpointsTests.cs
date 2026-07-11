using System.Reflection;
using FluentAssertions;
using SportRental.Admin.Api;
using SportRental.Admin.Services;

namespace SportRental.Admin.Tests.Api;

public class ConfirmationEndpointsTests
{
    [Theory]
    [InlineData("2026-07-10T08:00:00Z", "10.07.2026 10:00")]
    [InlineData("2026-01-10T08:00:00Z", "10.01.2026 09:00")]
    public void ConfirmationPage_UsesWarsawDaylightSavingTime(string utcValue, string expectedLocal)
    {
        var startUtc = DateTime.Parse(
            utcValue,
            null,
            System.Globalization.DateTimeStyles.AdjustToUniversal
                | System.Globalization.DateTimeStyles.AssumeUniversal);
        var data = new ConfirmationPageData(
            Guid.NewGuid(),
            "Jan Kowalski",
            "Test Rental",
            null,
            null,
            startUtc,
            startUtc.AddHours(2),
            100,
            0,
            new List<ConfirmationItemData>(),
            null,
            false,
            false);
        var method = typeof(ConfirmationEndpoints)
            .GetMethod("RenderConfirmationPage", BindingFlags.Static | BindingFlags.NonPublic);

        var html = method!.Invoke(null, new object[] { data, "test-token" }) as string;

        html.Should().Contain(expectedLocal);
    }
}
