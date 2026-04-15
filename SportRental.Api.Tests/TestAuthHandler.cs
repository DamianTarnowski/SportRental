using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SportRental.Api.Tests;

internal class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "TestAuth";
    public const string CustomerIdHeader = "X-Test-Customer-Id";
    public const string RoleHeader = "X-Test-Role";
    public const string EmailHeader = "X-Test-Email";

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var authHeader) ||
            !string.Equals(authHeader.ToString(), SchemeName, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var tenantId = Guid.Empty;
        if (Request.Headers.TryGetValue("X-Tenant-Id", out var tenantValues) &&
            Guid.TryParse(tenantValues.FirstOrDefault(), out var parsedTenant))
        {
            tenantId = parsedTenant;
        }

        var role = Request.Headers.TryGetValue(RoleHeader, out var roleValues)
            ? roleValues.FirstOrDefault() ?? "Client"
            : "Client";

        var email = Request.Headers.TryGetValue(EmailHeader, out var emailValues)
            ? emailValues.FirstOrDefault() ?? "test@example.com"
            : "test@example.com";

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "test-user"),
            new(ClaimTypes.Email, email),
            new(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Email, email),
            new("tenant-id", tenantId == Guid.Empty ? Guid.NewGuid().ToString() : tenantId.ToString()),
            new(ClaimTypes.Role, role)
        };

        if (Request.Headers.TryGetValue(CustomerIdHeader, out var customerValues) &&
            Guid.TryParse(customerValues.FirstOrDefault(), out var parsedCustomer) &&
            parsedCustomer != Guid.Empty)
        {
            claims.Add(new Claim("customer-id", parsedCustomer.ToString()));
        }

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
