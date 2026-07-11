using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace SportRental.Admin.Tests;

public sealed class DependencyLifetimeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public DependencyLifetimeTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));
    }

    [Fact]
    public void IdentityEmailAdapter_IsScopedWithTenantAwareSender()
    {
        using var firstScope = _factory.Services.CreateScope();
        using var secondScope = _factory.Services.CreateScope();

        var firstTenantAware = firstScope.ServiceProvider
            .GetRequiredService<SportRental.Admin.Services.Email.IEmailSender>();
        var firstIdentity = firstScope.ServiceProvider
            .GetRequiredService<Microsoft.AspNetCore.Identity.UI.Services.IEmailSender>();
        var secondIdentity = secondScope.ServiceProvider
            .GetRequiredService<Microsoft.AspNetCore.Identity.UI.Services.IEmailSender>();

        Assert.Same(firstTenantAware, firstIdentity);
        Assert.NotSame(firstIdentity, secondIdentity);
    }
}
