using System.Text.RegularExpressions;
using FluentAssertions;

namespace SportRental.Client.Tests.Services;

public sealed class ProductCatalogSourceTests
{
    [Fact]
    public void ProductImages_ExposeKnownFallbackBeforeUsingPlaceholder()
    {
        var clientRoot = FindClientProjectRoot();
        var index = File.ReadAllText(Path.Combine(clientRoot, "wwwroot", "index.html"));

        index.Should().Contain("img.dataset.fallbackSrc");
        index.Should().Contain("img.dataset.fallbackAttempted");
        index.Should().Contain("img.removeAttribute(\"srcset\")");
        index.IndexOf("img.src = fallback", StringComparison.Ordinal).Should().BeLessThan(
            index.IndexOf("data:image/svg+xml", StringComparison.Ordinal));

        var presentationFiles = new[]
        {
            Path.Combine(clientRoot, "Pages", "Products.razor"),
            Path.Combine(clientRoot, "Pages", "ProductDetails.razor"),
            Path.Combine(clientRoot, "Components", "ProductDetailsDialog.razor")
        };
        var productImages = presentationFiles
            .SelectMany(path => Regex.Matches(
                    File.ReadAllText(path),
                    "<img\\b[^>]*onerror=\"srImg\\(this\\)\"[^>]*>",
                    RegexOptions.Singleline | RegexOptions.CultureInvariant)
                .Select(match => $"{Path.GetFileName(path)}: {match.Value}"))
            .ToList();

        productImages.Should().NotBeEmpty();
        productImages.Should().OnlyContain(markup => markup.Contains(
            "data-fallback-src=",
            StringComparison.Ordinal));
    }

    [Fact]
    public void ProductCatalog_AvoidsNestedInteractiveCardsAndLowContrastCoralActions()
    {
        var clientRoot = FindClientProjectRoot();
        var products = File.ReadAllText(Path.Combine(clientRoot, "Pages", "Products.razor"));
        var css = File.ReadAllText(Path.Combine(clientRoot, "wwwroot", "css", "products.css"));

        products.Should().Contain("<article class=\"product-card\">");
        products.Should().Contain("<article class=\"product-mobile-card\">");
        products.Should().NotContain("<div class=\"product-card\"\n                     role=");
        products.Should().NotContain("<div class=\"product-mobile-card\"\n                         role=");
        products.Should().Contain("class=\"product-mobile-details-btn\"");
        products.Should().Contain("class=\"product-mobile-card-image\"");
        products.Should().Contain("class=\"product-mobile-card-actions\"");
        products.Should().NotContain("class=\"product-mobile-image\"");
        products.Should().NotContain("class=\"product-mobile-actions\"");

        css.Should().Contain(".product-card:focus-within .product-card-actions-overlay");
        css.Should().Contain(".product-mobile-card-actions");
        css.Should().NotContain(".product-mobile-actions {");
        css.Should().Contain("background: #C83645;");
        css.Should().NotContain("background: #F96167;");
        css.Should().NotContain("linear-gradient(135deg, #F96167");
    }

    private static string FindClientProjectRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "SportRental.Client");
            if (File.Exists(Path.Combine(candidate, "SportRental.Client.csproj")))
                return candidate;
        }

        throw new DirectoryNotFoundException("Nie znaleziono katalogu projektu SportRental.Client.");
    }
}
