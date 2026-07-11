using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;
using SportRental.Infrastructure.Tenancy;
using SportRental.Admin.Services.Auth;
using SportRental.Shared.Identity;

namespace SportRental.Admin.Tests.Api;

/// <summary>
/// Weryfikacja, że [Authorize] endpointy faktycznie odrzucają żądania anonimowe
/// (regresja na fixach SEC-A03 z commitów 0b7af06, 502316f, 05fbdd6).
/// Każdy test z dedykowanym WebApplicationFactory żeby auth scheme nie był współdzielony.
///
/// UWAGA: WebApplicationFactory boot trwa ~13s na test i bywa czuły na kolejność,
/// więc lepiej puszczać `dotnet test --filter "FullyQualifiedName~SecurityIntegrationTests"`
/// jako osobny krok, nie jako część szybkiego unit-test runa.
/// </summary>
[Trait("Category", "RequiresWebFactory")]
public sealed class SecurityIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string TestAuthScheme = "TestAuth";
    private const string AuthHeaderName = "X-Test-Auth";
    private const string RolesHeaderName = "X-Test-Roles";
    private const string TenantHeaderName = "X-Test-Tenant";
    private static readonly Guid TestTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherTenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly WebApplicationFactory<Program> _factory;

    public SecurityIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Tenant:Id"] = TestTenantId.ToString()
                });
            });
            builder.ConfigureTestServices(services =>
            {
                var toRemove = services
                    .Where(d =>
                        d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>) ||
                        d.ServiceType == typeof(IDbContextFactory<ApplicationDbContext>) ||
                        (d.ServiceType.IsGenericType && d.ServiceType.GetGenericArguments().Contains(typeof(ApplicationDbContext))))
                    .ToList();
                foreach (var descriptor in toRemove) services.Remove(descriptor);

                services.AddDbContext<ApplicationDbContext>(o => o
                    .UseInMemoryDatabase("security-tests")
                    .ConfigureWarnings(b => b.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
                services.AddScoped<IDbContextFactory<ApplicationDbContext>, ScopedDbContextFactory>();

                services.RemoveAll<ITenantProvider>();
                services.AddScoped<ITenantProvider>(_ => new TestTenantProvider(TestTenantId));

                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthScheme;
                    options.DefaultChallengeScheme = TestAuthScheme;
                }).AddScheme<AuthenticationSchemeOptions, ConfigurableTestAuthHandler>(
                    TestAuthScheme, _ => { });
            });
        });
    }

    [Fact]
    public async Task GetMyRentals_AnonymousRequest_Returns401()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync("/api/my-rentals");
        AssertUnauthorizedOrForbidden(response, "GET /api/my-rentals");
    }

    [Fact]
    public async Task GetCustomerTrust_AnonymousRequest_Returns401()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync("/api/customers/me/trust");
        AssertUnauthorizedOrForbidden(response, "GET /api/customers/me/trust");
    }

    [Fact]
    public async Task GetAdminReviews_AnonymousRequest_Returns401()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync("/api/admin/reviews");
        AssertUnauthorizedOrForbidden(response, "GET /api/admin/reviews");
    }

    [Fact]
    public async Task GetAuthState_RepeatedAnonymousChecks_DoNotConsumeCredentialLimit()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var responses = new List<HttpResponseMessage>();
        for (var i = 0; i < 6; i++)
        {
            responses.Add(await client.GetAsync("/api/auth/me"));
        }

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode));
    }

    [Fact]
    public async Task PostRentals_AnonymousRequest_Returns401()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.PostAsJsonAsync("/api/rentals", new { });
        AssertUnauthorizedOrForbidden(response, "POST /api/rentals");
    }

    [Fact]
    public async Task PostReviews_AnonymousRequest_Returns401()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.PostAsJsonAsync("/api/reviews", new { });
        AssertUnauthorizedOrForbidden(response, "POST /api/reviews");
    }

    [Theory]
    [InlineData(RentalStatus.Draft)]
    [InlineData(RentalStatus.Pending)]
    [InlineData(RentalStatus.Confirmed)]
    [InlineData(RentalStatus.Active)]
    [InlineData(RentalStatus.Cancelled)]
    public async Task PostReviews_RentalNotCompleted_ReturnsBadRequest(RentalStatus status)
    {
        var (rentalId, customerId) = await SeedReviewRentalAsync(status);
        using var client = await CreateGuestCustomerClientAsync(customerId);

        var response = await client.PostAsJsonAsync("/api/reviews", new
        {
            rentalId,
            qualityScore = 8,
            priceScore = 7,
            serviceScore = 9,
            comment = "Test"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.RentalReviews.IgnoreQueryFilters().AnyAsync(r => r.RentalId == rentalId));
    }

    [Fact]
    public async Task PostReviews_CompletedRental_ReturnsCreated()
    {
        var (rentalId, customerId) = await SeedReviewRentalAsync(RentalStatus.Completed);
        using var client = await CreateGuestCustomerClientAsync(customerId);

        var response = await client.PostAsJsonAsync("/api/reviews", new
        {
            rentalId,
            qualityScore = 8,
            priceScore = 7,
            serviceScore = 9,
            comment = "Test"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.True(await db.RentalReviews.IgnoreQueryFilters().AnyAsync(r => r.RentalId == rentalId));
    }

    [Fact]
    public async Task PostAdminCustomerReviews_AnonymousRequest_Returns401()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.PostAsJsonAsync("/api/admin/customer-reviews", new { });
        AssertUnauthorizedOrForbidden(response, "POST /api/admin/customer-reviews");
    }

    private static void AssertUnauthorizedOrForbidden(HttpResponseMessage response, string label)
    {
        // Anonim NIE może mieć successful response (200/201/204). Akceptowane są:
        //  - 401 Unauthorized / 403 Forbidden — czyste API
        //  - 302 Found — challenge cookie schemy redirektuje na /Account/Login
        //    (nie idealne, ale z punktu widzenia bezpieczeństwa równoważne odmowie dostępu)
        Assert.True(
            response.StatusCode == HttpStatusCode.Unauthorized ||
            response.StatusCode == HttpStatusCode.Forbidden ||
            response.StatusCode == HttpStatusCode.Found ||
            response.StatusCode == HttpStatusCode.Redirect,
            $"Expected 401/403/302 for {label}, got {(int)response.StatusCode} {response.StatusCode}");
    }

    [Theory]
    [InlineData("/api/products/{id}/image")]
    [InlineData("/api/tenants/{id}/logo")]
    public async Task OwnerOnlyEndpoint_NonOwner_Returns403(string pathTemplate)
    {
        var path = pathTemplate.Replace("{id}", Guid.NewGuid().ToString());
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(AuthHeaderName, "true");
        client.DefaultRequestHeaders.Add(RolesHeaderName, "Pracownik"); // bez Owner

        var response = await client.PostAsync(path, new MultipartFormDataContent());

        Assert.True(
            response.StatusCode == HttpStatusCode.Forbidden ||
            response.StatusCode == HttpStatusCode.Unauthorized,
            $"Non-owner expected 401/403 for POST {path}, got {(int)response.StatusCode}");
    }

    [Fact]
    public async Task CrossTenantRead_Rentals_HidesOtherTenantData()
    {
        // Seed wynajem należący do tenant B; zalogowany jako tenant A.
        await ResetDbAsync();
        var otherTenantRentalId = Guid.NewGuid();
        var otherCustomerId = Guid.NewGuid();
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Tenants.Add(new Tenant { Id = OtherTenantId, Name = "Tenant B" });
            db.Customers.Add(new Customer
            {
                Id = otherCustomerId,
                TenantId = OtherTenantId,
                FullName = "Other Tenant Customer",
                Email = "other@x.pl"
            });
            db.Rentals.Add(new Rental
            {
                Id = otherTenantRentalId,
                TenantId = OtherTenantId,
                CustomerId = otherCustomerId,
                StartDateUtc = DateTime.UtcNow.AddDays(-1),
                EndDateUtc = DateTime.UtcNow.AddDays(1),
                Status = RentalStatus.Active,
                TotalAmount = 100m
            });
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var tokenService = scope.ServiceProvider.GetRequiredService<JwtTokenService>();
            var token = tokenService.CreateUserToken(
                new ApplicationUser { Id = Guid.NewGuid(), Email = "owner-a@example.test" },
                TestTenantId,
                [RoleNames.Owner]);
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token.AccessToken);
        }

        // DELETE bo to jeden z prostszych endpointów authorized — właściciel tenant-a A powinien
        // dostać 404 (nie ma takiego rentalu w tenant A), nie 200/204.
        var response = await client.DeleteAsync($"/api/rentals/{otherTenantRentalId}");

        Assert.True(
            response.StatusCode == HttpStatusCode.NotFound ||
            response.StatusCode == HttpStatusCode.Forbidden,
            $"Cross-tenant DELETE expected 404/403 (data hidden), got {(int)response.StatusCode}");
    }

    [Fact]
    public async Task PublicEndpoint_AnonymousRequest_Returns200()
    {
        // Smoke: anonimowy GET /api/products powinien działać (publiczny katalog).
        await ResetDbAsync();
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/api/products");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ClientRole_CannotCreateRentalDirectlyWithoutCheckout()
    {
        await ResetDbAsync();
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(AuthHeaderName, "true");
        client.DefaultRequestHeaders.Add(RolesHeaderName, "Client");
        client.DefaultRequestHeaders.Add(TenantHeaderName, TestTenantId.ToString());

        var response = await client.PostAsJsonAsync("/api/rentals", new { });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task TenantOwner_CannotRefreshOrDeleteOtherTenantHold()
    {
        await ResetDbAsync();
        var productId = Guid.NewGuid();
        var holdId = Guid.NewGuid();
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Tenants.Add(new Tenant { Id = OtherTenantId, Name = "Tenant B" });
            db.Products.Add(new Product
            {
                Id = productId,
                TenantId = OtherTenantId,
                Name = "Sprzęt tenant B",
                Sku = $"B-{Guid.NewGuid():N}",
                DailyPrice = 50m,
                AvailableQuantity = 5,
                Available = true,
                IsActive = true
            });
            db.ReservationHolds.Add(new ReservationHold
            {
                Id = holdId,
                TenantId = OtherTenantId,
                ProductId = productId,
                Quantity = 1,
                StartDateUtc = DateTime.UtcNow.AddDays(2),
                EndDateUtc = DateTime.UtcNow.AddDays(3),
                CreatedAtUtc = DateTime.UtcNow,
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(10),
                SessionId = "other-tenant-session-id-1234567890"
            });
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(AuthHeaderName, "true");
        client.DefaultRequestHeaders.Add(RolesHeaderName, RoleNames.Owner);
        client.DefaultRequestHeaders.Add(TenantHeaderName, TestTenantId.ToString());

        var refresh = await client.PostAsync($"/api/holds/{holdId}/refresh?ttlMinutes=10", null);
        var delete = await client.DeleteAsync($"/api/holds/{holdId}");

        Assert.Equal(HttpStatusCode.NotFound, refresh.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
        await using var verificationScope = _factory.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.True(await verificationDb.ReservationHolds.IgnoreQueryFilters().AnyAsync(h => h.Id == holdId));
    }

    private async Task ResetDbAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        if (!await db.Tenants.AnyAsync(t => t.Id == TestTenantId))
        {
            db.Tenants.Add(new Tenant { Id = TestTenantId, Name = "Tenant A" });
            await db.SaveChangesAsync();
        }
    }

    private async Task<(Guid RentalId, Guid CustomerId)> SeedReviewRentalAsync(RentalStatus status)
    {
        await ResetDbAsync();
        var customerId = Guid.NewGuid();
        var rentalId = Guid.NewGuid();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Customers.Add(new Customer
        {
            Id = customerId,
            TenantId = TestTenantId,
            FullName = "Klient opinii",
            Email = "reviewer@example.test"
        });
        db.Rentals.Add(new Rental
        {
            Id = rentalId,
            TenantId = TestTenantId,
            CustomerId = customerId,
            StartDateUtc = DateTime.UtcNow.AddDays(-2),
            EndDateUtc = DateTime.UtcNow.AddDays(-1),
            ReturnedAtUtc = status == RentalStatus.Completed ? DateTime.UtcNow.AddDays(-1) : null,
            Status = status,
            TotalAmount = 100m,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-3)
        });
        await db.SaveChangesAsync();

        return (rentalId, customerId);
    }

    private async Task<HttpClient> CreateGuestCustomerClientAsync(Guid customerId)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await using var scope = _factory.Services.CreateAsyncScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<JwtTokenService>();
        var token = tokenService.CreateGuestToken(customerId, TestTenantId, "reviewer@example.test");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return client;
    }

    private sealed class TestTenantProvider : ITenantProvider
    {
        private readonly Guid _tenantId;
        public TestTenantProvider(Guid tenantId) => _tenantId = tenantId;
        public Guid? GetCurrentTenantId() => _tenantId;
    }

    private sealed class ScopedDbContextFactory : IDbContextFactory<ApplicationDbContext>
    {
        private readonly DbContextOptions<ApplicationDbContext> _options;

        public ScopedDbContextFactory(DbContextOptions<ApplicationDbContext> options) =>
            _options = options;

        public ApplicationDbContext CreateDbContext() => new(_options);

        public ValueTask<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => new(CreateDbContext());
    }

    private sealed class ConfigurableTestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public ConfigurableTestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
            : base(options, logger, encoder) { }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(AuthHeaderName, out var values) || values.Count == 0)
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var roles = Request.Headers.TryGetValue(RolesHeaderName, out var rv)
                ? rv.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : new[] { "Owner", "SuperAdmin" };

            var tenant = Request.Headers.TryGetValue(TenantHeaderName, out var tv)
                ? tv.ToString()
                : TestTenantId.ToString();

            var claims = new List<Claim>
            {
                new(ClaimTypes.Name, "test"),
                new("tenant-id", tenant)
            };
            foreach (var r in roles) claims.Add(new Claim(ClaimTypes.Role, r));

            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
