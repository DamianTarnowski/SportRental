using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Moq;
using SportRental.Admin.Services.Tenancy;

namespace SportRental.Admin.Tests.Services.Tenancy;

public class BlazorTenantProviderTests
{
    [Fact]
    public void GetCurrentTenantId_ForAnonymousHttpRequest_DoesNotQueryBlazorAuthenticationState()
    {
        var httpContext = new DefaultHttpContext();
        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var authState = new Mock<AuthenticationStateProvider>(MockBehavior.Strict);
        var provider = new BlazorTenantProvider(accessor, authState.Object);

        var result = provider.GetCurrentTenantId();

        result.Should().BeNull();
        authState.Verify(x => x.GetAuthenticationStateAsync(), Times.Never);
    }

    [Fact]
    public void GetCurrentTenantId_WithHttpHeader_ReturnsHeaderTenantWithoutQueryingBlazorState()
    {
        var tenantId = Guid.NewGuid();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Tenant-Id"] = tenantId.ToString();
        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var authState = new Mock<AuthenticationStateProvider>(MockBehavior.Strict);
        var provider = new BlazorTenantProvider(accessor, authState.Object);

        var result = provider.GetCurrentTenantId();

        result.Should().Be(tenantId);
        authState.Verify(x => x.GetAuthenticationStateAsync(), Times.Never);
    }

    [Fact]
    public void GetCurrentTenantId_ForAuthenticatedRequest_PrefersSignedClaimOverHeader()
    {
        var claimedTenantId = Guid.NewGuid();
        var headerTenantId = Guid.NewGuid();
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("tenant-id", claimedTenantId.ToString())],
                authenticationType: "Test"))
        };
        httpContext.Request.Headers["X-Tenant-Id"] = headerTenantId.ToString();
        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var authState = new Mock<AuthenticationStateProvider>(MockBehavior.Strict);
        var provider = new BlazorTenantProvider(accessor, authState.Object);

        var result = provider.GetCurrentTenantId();

        result.Should().Be(claimedTenantId);
        result.Should().NotBe(headerTenantId);
        authState.Verify(x => x.GetAuthenticationStateAsync(), Times.Never);
    }

    [Fact]
    public void GetCurrentTenantId_WithoutHttpContext_UsesBlazorAuthenticationState()
    {
        var tenantId = Guid.NewGuid();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("tenant-id", tenantId.ToString())],
            authenticationType: "Test"));
        var accessor = new HttpContextAccessor();
        var authState = new Mock<AuthenticationStateProvider>();
        authState.Setup(x => x.GetAuthenticationStateAsync())
            .ReturnsAsync(new AuthenticationState(principal));
        var provider = new BlazorTenantProvider(accessor, authState.Object);

        var result = provider.GetCurrentTenantId();

        result.Should().Be(tenantId);
    }
}
