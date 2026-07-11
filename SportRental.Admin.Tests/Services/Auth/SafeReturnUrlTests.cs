using FluentAssertions;
using SportRental.Admin.Services.Auth;

namespace SportRental.Admin.Tests.Services.Auth;

public sealed class SafeReturnUrlTests
{
    [Theory]
    [InlineData("/_client/products?category=Narty", "/_client/products?category=Narty")]
    [InlineData("/_client", "/_client")]
    [InlineData("~/dashboard", "/dashboard")]
    [InlineData("dashboard", "/dashboard")]
    public void ResolveLocal_AllowsWellFormedLocalPaths(string input, string expected)
    {
        SafeReturnUrl.ResolveLocal(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("//evil.example")]
    [InlineData("https://evil.example")]
    [InlineData("\\\\evil.example")]
    [InlineData("/%2Fevil.example")]
    [InlineData("/%5Cevil.example")]
    [InlineData("/ok\r\nLocation: https://evil.example")]
    public void ResolveLocal_RejectsExternalOrAmbiguousPaths(string input)
    {
        SafeReturnUrl.ResolveLocal(input, "/safe").Should().Be("/safe");
    }

    [Theory]
    [InlineData("/_client/products", "/_client/products")]
    [InlineData("/_CLIENT?view=map", "/_CLIENT?view=map")]
    [InlineData("/dashboard", SafeReturnUrl.ClientFallback)]
    [InlineData("/_client-evil", SafeReturnUrl.ClientFallback)]
    [InlineData("//evil.example", SafeReturnUrl.ClientFallback)]
    public void ResolveClient_RequiresClientPathBoundary(string input, string expected)
    {
        SafeReturnUrl.ResolveClient(input).Should().Be(expected);
    }
}
