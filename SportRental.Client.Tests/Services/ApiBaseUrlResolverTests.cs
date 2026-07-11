using FluentAssertions;
using SportRental.Client.Services;

namespace SportRental.Client.Tests.Services;

public class ApiBaseUrlResolverTests
{
    [Theory]
    [InlineData("http://127.0.0.1:5501/_client/", "https://stale.example", "http://127.0.0.1:5501")]
    [InlineData("https://app.example.com/_client/", "", "https://app.example.com")]
    [InlineData("https://app.example.com/_CLIENT", null, "https://app.example.com")]
    public void Resolve_BundledClient_AlwaysUsesAdminOrigin(
        string hostAddress,
        string? configuredBaseUrl,
        string expected)
    {
        ApiBaseUrlResolver.Resolve(hostAddress, configuredBaseUrl).Should().Be(expected);
    }

    [Fact]
    public void Resolve_StandaloneClient_UsesConfiguredApiUrl()
    {
        var result = ApiBaseUrlResolver.Resolve(
            "http://localhost:5014/",
            "http://localhost:5001/");

        result.Should().Be("http://localhost:5001");
    }

    [Fact]
    public void Resolve_StandaloneClientWithoutConfiguration_UsesDevelopmentFallback()
    {
        var result = ApiBaseUrlResolver.Resolve("http://localhost:5014/", "  ");

        result.Should().Be("http://localhost:5001");
    }
}
