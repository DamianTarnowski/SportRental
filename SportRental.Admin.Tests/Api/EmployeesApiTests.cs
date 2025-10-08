using SportRental.Admin.Api;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;
using SportRental.Infrastructure.Tenancy;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SportRental.Shared.Identity;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Xunit;

namespace SportRental.Admin.Tests.Api;

public class EmployeesApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string TestAuthScheme = "TestAuth";
    private const string AuthHeaderName = "X-Test-Auth";
    private static readonly Guid TestTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly WebApplicationFactory<Program> _factory;

    public EmployeesApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Tenant:Id"] = TestTenantId.ToString()
                });
            });

            builder.ConfigureTestServices(services =>
            {
                var descriptorsToRemove = services
                    .Where(d =>
                        d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>) ||
                        d.ServiceType == typeof(IDbContextFactory<ApplicationDbContext>) ||
                        d.ServiceType == typeof(ApplicationDbContext) ||
                        (d.ServiceType.IsGenericType && d.ServiceType.GetGenericArguments().Contains(typeof(ApplicationDbContext))) ||
                        (d.ServiceType.FullName?.StartsWith("Microsoft.EntityFrameworkCore.Internal") ?? false))
                    .ToList();

                foreach (var descriptor in descriptorsToRemove)
                {
                    services.Remove(descriptor);
                }

                var databaseName = $"employees-api-tests-{Guid.NewGuid()}";

                services.AddDbContext<ApplicationDbContext>(options => options
                    .UseInMemoryDatabase(databaseName)
                    .ConfigureWarnings(builder => builder.Ignore(InMemoryEventId.TransactionIgnoredWarning)));

                services.AddScoped<IDbContextFactory<ApplicationDbContext>, TestDbContextFactory>();

                services.RemoveAll<ITenantProvider>();
                services.AddScoped<ITenantProvider>(_ => new TestTenantProvider(TestTenantId));

                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthScheme;
                    options.DefaultChallengeScheme = TestAuthScheme;
                }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthScheme, _ => { });

                services.AddAuthorization(options =>
                {
                    options.DefaultPolicy = new AuthorizationPolicyBuilder(TestAuthScheme)
                        .RequireAuthenticatedUser()
                        .Build();
                });

                services.RemoveAll<IHostedService>();
            });
        });
    }

    [Fact]
    public async Task GetEmployees_ShouldReturnOkWithEmptyList()
    {
        using var client = await CreateClientAsync();

        var response = await client.GetAsync("/api/employees");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadAsByteArrayAsync();
        var employees = JsonSerializer.Deserialize<List<JsonElement>>(payload, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        employees.Should().NotBeNull();
        employees!.Should().BeEmpty();
    }

    [Fact]
    public async Task PostEmployee_WithValidData_ShouldReturnCreated()
    {
        using var client = await CreateClientAsync();

        var request = new CreateEmployeeRequest
        {
            FullName = "Jan Kowalski",
            Email = "jan.kowalski@test.com",
            City = "Warszawa",
            Telephone = "+48123456789",
            Position = "Pracownik",
            Role = EmployeeRole.Pracownik,
            Comment = "Test employee"
        };

        var response = await client.PostAsJsonAsync("/api/employees", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        created.TryGetProperty("id", out var idProperty).Should().BeTrue();
        Guid.TryParse(idProperty.GetString(), out var employeeId).Should().BeTrue();

        var getResponse = await client.GetAsync($"/api/employees/{employeeId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PutEmployee_WithValidData_ShouldReturnOk()
    {
        using var client = await CreateClientAsync();

        var createResponse = await client.PostAsJsonAsync("/api/employees", new CreateEmployeeRequest
        {
            FullName = "Anna Nowak",
            Email = "anna.nowak@test.com",
            City = "Krakow",
            Telephone = "+48123450000",
            Position = "Kierownik",
            Role = EmployeeRole.Kierownik
        });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var employeeId = Guid.Parse(created.GetProperty("id").GetString()!);

        var updateResponse = await client.PutAsJsonAsync($"/api/employees/{employeeId}", new UpdateEmployeeRequest
        {
            FullName = "Anna Nowak",
            Email = "anna.nowak@test.com",
            City = "Krakow",
            Telephone = "+48123450001",
            Position = "Manager",
            Role = EmployeeRole.Manager
        });

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PostEmployee_WithInvalidEmail_ShouldReturnBadRequest()
    {
        using var client = await CreateClientAsync();

        var response = await client.PostAsJsonAsync("/api/employees", new CreateEmployeeRequest
        {
            FullName = "Jan Test",
            Email = "not-an-email",
            City = "Warszawa",
            Telephone = "+48123123123",
            Position = "Pracownik",
            Role = EmployeeRole.Pracownik
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task<HttpClient> CreateClientAsync(bool authenticated = true)
    {
        await ResetDatabaseAsync();

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        if (authenticated)
        {
            client.DefaultRequestHeaders.Add(AuthHeaderName, "true");
        }

        return client;
    }

    private async Task ResetDatabaseAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        foreach (var role in new[] { RoleNames.SuperAdmin, RoleNames.Owner, RoleNames.Employee, RoleNames.Client })
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                var identityRole = new IdentityRole<Guid> { Name = role, NormalizedName = role.ToUpperInvariant() };
                var result = await roleManager.CreateAsync(identityRole);
                if (!result.Succeeded)
                {
                    throw new InvalidOperationException($"Failed to seed role {role}: {string.Join(", ", result.Errors.Select(e => e.Description))}");
                }
            }
        }

        if (!await db.Tenants.AnyAsync(t => t.Id == TestTenantId))
        {
            db.Tenants.Add(new Tenant
            {
                Id = TestTenantId,
                Name = "Test Tenant"
            });
            await db.SaveChangesAsync();
        }
    }

    private sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(AuthHeaderName, out var values) || values.Count == 0)
            {
                return Task.FromResult(AuthenticateResult.Fail("Missing test auth header."));
            }

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "integration-test"),
                new Claim(ClaimTypes.Name, "integration@test.com"),
                new Claim(ClaimTypes.Role, RoleNames.SuperAdmin),
                new Claim(ClaimTypes.Role, RoleNames.Owner),
                new Claim("tenant-id", TestTenantId.ToString())
            };

            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    private sealed class TestDbContextFactory : IDbContextFactory<ApplicationDbContext>
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public TestDbContextFactory(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        public ApplicationDbContext CreateDbContext()
        {
            using var scope = _scopeFactory.CreateScope();
            var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>();
            return new ApplicationDbContext(options);
        }

        public ValueTask<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => new(CreateDbContext());
    }

    private sealed class TestTenantProvider : ITenantProvider
    {
        private readonly Guid _tenantId;

        public TestTenantProvider(Guid tenantId)
        {
            _tenantId = tenantId;
        }

        public Guid? GetCurrentTenantId() => _tenantId;
    }
}
