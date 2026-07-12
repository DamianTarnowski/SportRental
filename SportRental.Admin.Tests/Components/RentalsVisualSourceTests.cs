using FluentAssertions;

namespace SportRental.Admin.Tests.Components;

public class RentalsVisualSourceTests
{
    [Fact]
    public void EquipmentPicker_HasDedicatedHighContrastDarkThemeSurface()
    {
        var source = LoadRentalsSource();

        source.Should().Contain("Class=\"pa-4 rs-rental-equipment-picker\"");
        source.Should().Contain("[data-theme=\"dark\"] .rs-rental-equipment-picker");
        source.Should().Contain("--rs-equipment-panel-bg: linear-gradient(135deg, #10261A 0%, #183424 100%)");
        source.Should().Contain("--rs-equipment-panel-fg: #F4FFF6");
        source.Should().Contain("--rs-equipment-panel-border: #8DB99A");
        source.Should().NotContain("background: linear-gradient(135deg, #e8f5e9 0%, #c8e6c9 100%)");

        ContrastRatio("#F4FFF6", "#10261A").Should().BeGreaterThanOrEqualTo(7d);
        ContrastRatio("#F4FFF6", "#183424").Should().BeGreaterThanOrEqualTo(7d);
        ContrastRatio("#8DB99A", "#183424").Should().BeGreaterThanOrEqualTo(3d);
    }

    private static string LoadRentalsSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "SportRental.Admin",
                "Components",
                "Pages",
                "Admin",
                "Rentals.razor");

            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Nie znaleziono pliku Rentals.razor względem katalogu testowego.");
    }

    private static double ContrastRatio(string foreground, string background)
    {
        var foregroundLuminance = RelativeLuminance(foreground);
        var backgroundLuminance = RelativeLuminance(background);
        var lighter = Math.Max(foregroundLuminance, backgroundLuminance);
        var darker = Math.Min(foregroundLuminance, backgroundLuminance);
        return (lighter + 0.05d) / (darker + 0.05d);
    }

    private static double RelativeLuminance(string hex)
    {
        var channels = new[] { 1, 3, 5 }
            .Select(index => Convert.ToInt32(hex.Substring(index, 2), 16) / 255d)
            .Select(channel => channel <= 0.04045d
                ? channel / 12.92d
                : Math.Pow((channel + 0.055d) / 1.055d, 2.4d))
            .ToArray();

        return (0.2126d * channels[0]) + (0.7152d * channels[1]) + (0.0722d * channels[2]);
    }
}
