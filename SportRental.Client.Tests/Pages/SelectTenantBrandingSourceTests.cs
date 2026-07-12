using FluentAssertions;

namespace SportRental.Client.Tests.Pages;

public sealed class SelectTenantBrandingSourceTests
{
    [Fact]
    public void TenantGradient_InterpolatesOnlyNormalizedColors()
    {
        var source = LoadSource();

        source.Should().Contain("BrandColor.NormalizeOrDefault(tenant.PrimaryColor");
        source.Should().Contain("BrandColor.NormalizeOrDefault(tenant.SecondaryColor");
        source.Should().NotContain("{tenant.PrimaryColor} 0%");
        source.Should().NotContain("{tenant.SecondaryColor} 100%");
    }

    private static string LoadSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "SportRental.Client",
                "Pages",
                "SelectTenant.razor");

            if (File.Exists(candidate))
                return File.ReadAllText(candidate);

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Nie znaleziono pliku SelectTenant.razor względem katalogu testowego.");
    }
}
