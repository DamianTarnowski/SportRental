using FluentAssertions;
using Microsoft.AspNetCore.Components;
using SportRental.Admin.Components.Account;

namespace SportRental.Admin.Tests.Services.Auth;

public sealed class IdentityRedirectManagerTests
{
    [Theory]
    [InlineData("//evil.example", "/")]
    [InlineData("https://evil.example/path", "/")]
    [InlineData("https://app.example.test/dashboard?tab=1", "/dashboard?tab=1")]
    [InlineData("Account/Login", "/Account/Login")]
    public void RedirectTo_NeverLeavesConfiguredOrigin(string input, string expected)
    {
        var navigation = new CapturingNavigationManager(
            "https://app.example.test/",
            "https://app.example.test/Account/Login");
        var manager = new IdentityRedirectManager(navigation);

        var action = () => manager.RedirectTo(input);

        action.Should().Throw<InvalidOperationException>();
        navigation.LastNavigation.Should().Be(expected);
    }

    private sealed class CapturingNavigationManager : NavigationManager
    {
        public CapturingNavigationManager(string baseUri, string currentUri)
        {
            Initialize(baseUri, currentUri);
        }

        public string? LastNavigation { get; private set; }

        protected override void NavigateToCore(string uri, bool forceLoad)
        {
            LastNavigation = uri;
        }
    }
}
