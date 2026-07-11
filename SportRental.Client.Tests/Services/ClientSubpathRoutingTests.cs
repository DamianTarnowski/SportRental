using System.Text.RegularExpressions;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using SportRental.Client.Components;

namespace SportRental.Client.Tests.Services;

public sealed class ClientSubpathRoutingTests : TestContext
{
    [Fact]
    public void Breadcrumbs_RecognizesRouteAndKeepsLinksInsideBundledClientBasePath()
    {
        var productId = Guid.NewGuid();
        Services.AddSingleton<NavigationManager>(new SubpathNavigationManager(
            "https://app.example.test/_client/",
            $"https://app.example.test/_client/products/{productId:D}?source=map"));

        var component = RenderComponent<Breadcrumbs>();

        component.FindAll("a")
            .Select(anchor => anchor.GetAttribute("href"))
            .Should().Equal("./", "products");
        component.Markup.Should().Contain("Szczegóły produktu");
        component.Markup.Should().NotContain("href=\"/products\"");
    }

    [Fact]
    public void ClientNavigationLiterals_DoNotEscapeConfiguredBasePath()
    {
        var clientRoot = FindClientProjectRoot();
        var forbidden = new Regex(
            "(?:\\b(?:href|Href)\\s*=\\s*[\\\"']/|" +
            "\\bNavigateTo\\s*\\(\\s*\\$?[\\\"']/|" +
            "\\bUrl\\s*=\\s*[\\\"']/)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        var violations = EnumerateNavigationSourceFiles(clientRoot)
            .SelectMany(path => File.ReadLines(path)
                .Select((line, index) => new { Path = path, Line = index + 1, Text = line }))
            .Where(candidate => forbidden.IsMatch(candidate.Text))
            .Select(candidate =>
                $"{Path.GetRelativePath(clientRoot, candidate.Path)}:{candidate.Line}: {candidate.Text.Trim()}")
            .ToList();

        violations.Should().BeEmpty(
            "client routes must be relative to <base>; root-absolute /api, /Account and /_client server URLs are not client navigation literals");
    }

    private static IEnumerable<string> EnumerateNavigationSourceFiles(string clientRoot)
    {
        var codeDirectories = new[] { "Components", "Helpers", "Layout", "Pages", "Services" };
        var extensions = new[] { "*.razor", "*.cs" };

        var rootFiles = extensions.SelectMany(pattern =>
            Directory.EnumerateFiles(clientRoot, pattern, SearchOption.TopDirectoryOnly));
        var codeFiles = codeDirectories.SelectMany(directory => extensions.SelectMany(pattern =>
            Directory.EnumerateFiles(
                Path.Combine(clientRoot, directory),
                pattern,
                SearchOption.AllDirectories)));
        var interopFiles = Directory.EnumerateFiles(
            Path.Combine(clientRoot, "wwwroot", "js"),
            "*.js",
            SearchOption.AllDirectories);

        return rootFiles.Concat(codeFiles).Concat(interopFiles);
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

    private sealed class SubpathNavigationManager : NavigationManager
    {
        public SubpathNavigationManager(string baseUri, string uri)
        {
            Initialize(baseUri, uri);
        }

        protected override void NavigateToCore(string uri, bool forceLoad)
        {
            Uri = ToAbsoluteUri(uri).AbsoluteUri;
            NotifyLocationChanged(isInterceptedLink: false);
        }
    }
}
