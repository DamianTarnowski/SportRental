using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Moq;
using SportRental.Admin.Services;

namespace SportRental.Admin.Tests.Services;

public sealed class ClientAppUrlResolverTests
{
    [Fact]
    public void Resolve_ExplicitClientUrl_RemainsAuthoritative()
    {
        var configuration = CreateConfiguration(
            "https://client.example.test/customer-app/");

        var result = ClientAppUrlResolver.Resolve(
            configuration,
            "https://admin.example.test");

        Assert.Equal("https://client.example.test/customer-app", result);
    }

    [Theory]
    [InlineData("https://admin.example.test", "https://admin.example.test/_client")]
    [InlineData("https://admin.example.test/", "https://admin.example.test/_client")]
    public void Resolve_WithoutExplicitClientUrl_UsesBundledClientPath(
        string adminBaseUrl,
        string expected)
    {
        var result = ClientAppUrlResolver.Resolve(
            CreateConfiguration(clientBaseUrl: null),
            adminBaseUrl);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Resolve_WithoutAnyPublicBaseUrl_ReturnsEmpty()
    {
        var result = ClientAppUrlResolver.Resolve(
            CreateConfiguration(clientBaseUrl: null),
            adminBaseUrl: null);

        Assert.Empty(result);
    }

    [Fact]
    public void TryResolveSecurityBaseUrl_ProductionUsesConfiguredHttpsClientUrl()
    {
        var configuration = CreateConfiguration(
            "https://client.example.test/customer-app/",
            adminBaseUrl: null);

        var success = ClientAppUrlResolver.TryResolveSecurityBaseUrl(
            configuration,
            CreateEnvironment(Environments.Production),
            out var result);

        Assert.True(success);
        Assert.Equal("https://client.example.test/customer-app", result);
    }

    [Fact]
    public void TryResolveSecurityBaseUrl_ProductionUsesConfiguredAdminBundledClient()
    {
        var configuration = CreateConfiguration(
            clientBaseUrl: null,
            adminBaseUrl: "https://admin.example.test/");

        var success = ClientAppUrlResolver.TryResolveSecurityBaseUrl(
            configuration,
            CreateEnvironment(Environments.Production),
            out var result);

        Assert.True(success);
        Assert.Equal("https://admin.example.test/_client", result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("http://client.example.test")]
    [InlineData("https://user:password@client.example.test")]
    [InlineData("https://client.example.test/?next=evil")]
    public void TryResolveSecurityBaseUrl_ProductionRejectsMissingOrUnsafeUrl(string? clientBaseUrl)
    {
        var success = ClientAppUrlResolver.TryResolveSecurityBaseUrl(
            CreateConfiguration(clientBaseUrl, adminBaseUrl: null),
            CreateEnvironment(Environments.Production),
            out var result);

        Assert.False(success);
        Assert.Empty(result);
    }

    [Fact]
    public void TryResolveSecurityBaseUrl_DevelopmentUsesFixedLoopbackFallback()
    {
        var success = ClientAppUrlResolver.TryResolveSecurityBaseUrl(
            CreateConfiguration(clientBaseUrl: null, adminBaseUrl: null),
            CreateEnvironment(Environments.Development),
            out var result);

        Assert.True(success);
        Assert.Equal("http://localhost:5014", result);
    }

    private static IConfiguration CreateConfiguration(string? clientBaseUrl, string? adminBaseUrl = null)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ClientApp:PublicBaseUrl"] = clientBaseUrl,
                ["Admin:PublicBaseUrl"] = adminBaseUrl
            })
            .Build();
    }

    private static IHostEnvironment CreateEnvironment(string name)
    {
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(candidate => candidate.EnvironmentName).Returns(name);
        return environment.Object;
    }
}
