using FluentAssertions;

namespace SportRental.Admin.Tests.Components;

public sealed class BrandingValidationSourceTests
{
    [Theory]
    [InlineData("CompanySettings.razor")]
    [InlineData("Owner.razor")]
    public void BrandingSave_ValidatesBothColorsBeforePersisting(string fileName)
    {
        var source = LoadAdminPage(fileName);

        source.Should().Contain("BrandColor.NormalizeHex");
        source.Should().Contain("Kolor główny musi mieć format #RRGGBB");
        source.Should().Contain("Kolor dodatkowy musi mieć format #RRGGBB");
    }

    [Theory]
    [InlineData("CompanySettings.razor")]
    [InlineData("Owner.razor")]
    public void LogoUpload_ReportsInvalidTypeSizeAndStorageFailure(string fileName)
    {
        var source = LoadAdminPage(fileName);

        source.Should().Contain("Logo musi być plikiem PNG, JPG lub WebP.");
        source.Should().Contain("Logo jest za duże. Maksymalny rozmiar to 5 MB.");
        source.Should().Contain("Nie udało się zapisać logo. Sprawdź plik i spróbuj ponownie.");
        source.Should().NotContain("catch { }");
    }

    private static string LoadAdminPage(string fileName)
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
                fileName);

            if (File.Exists(candidate))
                return File.ReadAllText(candidate);

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Nie znaleziono pliku {fileName} względem katalogu testowego.");
    }
}
