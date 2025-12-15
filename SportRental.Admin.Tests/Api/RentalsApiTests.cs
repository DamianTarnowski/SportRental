using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;
using SportRental.Infrastructure.Tenancy;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;namespace SportRental.Admin.Tests.Api;

public class RentalsApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string TestAuthScheme = "TestAuth";
    private const string AuthHeaderName = "X-Test-Auth";
    private static readonly Guid TestTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly WebApplicationFactory<Program> _factory;

    public RentalsApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((context, config) =>
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
                        (d.ServiceType.IsGenericType && d.ServiceType.GetGenericArguments().Contains(typeof(ApplicationDbContext))) ||
                        (d.ServiceType.FullName?.StartsWith("Microsoft.EntityFrameworkCore.Internal") ?? false))
                    .ToList();

                toRemove.AddRange(services.Where(d => d.ServiceType == typeof(ApplicationDbContext)));

                foreach (var descriptor in toRemove)
                {
                    services.Remove(descriptor);
                }

                services.AddDbContext<ApplicationDbContext>(options => options
                .UseInMemoryDatabase("rentals-api-tests")
                .ConfigureWarnings(b => b.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
                services.AddScoped<IDbContextFactory<ApplicationDbContext>, TestDbContextFactory>();

                services.RemoveAll(typeof(SportRental.Admin.Services.Contracts.IContractGenerator));
                services.RemoveAll(typeof(SportRental.Admin.Services.Storage.IFileStorage));
                services.RemoveAll(typeof(SportRental.Admin.Services.Sms.ISmsSender));
                services.TryAddScoped<SportRental.Admin.Services.Contracts.IContractGenerator, FakeContractGenerator>();
                services.TryAddSingleton<SportRental.Admin.Services.Storage.IFileStorage, FakeFileStorage>();
                services.TryAddSingleton<SportRental.Admin.Services.Sms.ISmsSender, FakeSmsSender>();

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
            });
        });
    }

    [Fact]
    public async Task GetProducts_ShouldReturnOk()
    {
        var client = await CreateClientAsync();

        var response = await client.GetAsync("/api/products");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PostRentals_WithoutAuth_ShouldReturnUnauthorized()
    {
        var client = await CreateClientAsync(authenticated: false);

        var request = new
        {
            CustomerId = Guid.NewGuid(),
            StartDateUtc = DateTime.UtcNow,
            EndDateUtc = DateTime.UtcNow.AddDays(1),
            Items = new[] { new { ProductId = Guid.NewGuid(), Quantity = 1 } }
        };

        var response = await client.PostAsJsonAsync("/api/rentals", request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostRentals_WithValidData_ShouldReturnCreated()
    {
        var client = await CreateClientAsync();

        Guid productId;
        Guid customerId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            productId = Guid.NewGuid();
            customerId = Guid.NewGuid();

            await db.Products.AddAsync(new Product
            {
                Id = productId,
                TenantId = TestTenantId,
                Name = "Test Product",
                Sku = "TEST-1",
                DailyPrice = 10,
                AvailableQuantity = 5,
                CreatedAtUtc = DateTime.UtcNow
            });

            await db.Customers.AddAsync(new Customer
            {
                Id = customerId,
                TenantId = TestTenantId,
                FullName = "Test Customer"
            });

            await db.SaveChangesAsync();
        }

        var request = new
        {
            CustomerId = customerId,
            StartDateUtc = DateTime.UtcNow.Date,
            EndDateUtc = DateTime.UtcNow.Date.AddDays(2),
            Items = new[] { new { ProductId = productId, Quantity = 2 } }
        };

        var response = await client.PostAsJsonAsync("/api/rentals", request);
        var payload = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = JsonDocument.Parse(payload);
        json.RootElement.TryGetProperty("contractUrl", out var contractUrl).Should().BeTrue();
        contractUrl.GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task PostRentals_WithMultipleItems_ShouldReturnCreated()
    {
        var client = await CreateClientAsync();

        Guid product1Id;
        Guid product2Id;
        Guid customerId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            product1Id = Guid.NewGuid();
            product2Id = Guid.NewGuid();
            customerId = Guid.NewGuid();

            await db.Products.AddRangeAsync(
                new Product { Id = product1Id, TenantId = TestTenantId, Name = "P1", Sku = "P1", DailyPrice = 20, AvailableQuantity = 10, CreatedAtUtc = DateTime.UtcNow },
                new Product { Id = product2Id, TenantId = TestTenantId, Name = "P2", Sku = "P2", DailyPrice = 15, AvailableQuantity = 5, CreatedAtUtc = DateTime.UtcNow }
            );
            await db.Customers.AddAsync(new Customer { Id = customerId, TenantId = TestTenantId, FullName = "Multi Items Customer" });
            await db.SaveChangesAsync();
        }

        var request = new
        {
            CustomerId = customerId,
            StartDateUtc = DateTime.UtcNow.Date,
            EndDateUtc = DateTime.UtcNow.Date.AddDays(3),
            Items = new[]
            {
                new { ProductId = product1Id, Quantity = 2 },
                new { ProductId = product2Id, Quantity = 1 },
            }
        };

        var response = await client.PostAsJsonAsync("/api/rentals", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task DeleteRental_ShouldReturnOk()
    {
        var client = await CreateClientAsync();

        Guid rentalId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var productId = Guid.NewGuid();
            var customerId = Guid.NewGuid();
            rentalId = Guid.NewGuid();

            await db.Products.AddAsync(new Product { Id = productId, TenantId = TestTenantId, Name = "To Delete", Sku = "DEL-1", DailyPrice = 10, AvailableQuantity = 3, CreatedAtUtc = DateTime.UtcNow });
            await db.Customers.AddAsync(new Customer { Id = customerId, TenantId = TestTenantId, FullName = "Del Customer" });
            await db.Rentals.AddAsync(new Rental
            {
                Id = rentalId,
                TenantId = TestTenantId,
                CustomerId = customerId,
                StartDateUtc = DateTime.UtcNow.Date,
                EndDateUtc = DateTime.UtcNow.Date.AddDays(1),
                Status = RentalStatus.Confirmed,
                CreatedAtUtc = DateTime.UtcNow,
                TotalAmount = 10
            });
            await db.RentalItems.AddAsync(new RentalItem
            {
                Id = Guid.NewGuid(),
                RentalId = rentalId,
                ProductId = productId,
                Quantity = 1,
                PricePerDay = 10,
                Subtotal = 10
            });
            await db.SaveChangesAsync();
        }

        var response = await client.DeleteAsync($"/api/rentals/{rentalId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
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
                new Claim(ClaimTypes.Role, "Owner"),
                new Claim(ClaimTypes.Role, "SuperAdmin"),
                new Claim("tenant-id", TestTenantId.ToString())
            };

            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
    private sealed class FakeContractGenerator : SportRental.Admin.Services.Contracts.IContractGenerator
    {
        public Task<byte[]> GenerateRentalContractAsync(Rental rental, IEnumerable<RentalItem> items, Customer customer, IEnumerable<Product> products, CompanyInfo? companyInfo = null, CancellationToken ct = default)
            => Task.FromResult(System.Text.Encoding.UTF8.GetBytes("PDF"));
        public Task<byte[]> GenerateRentalContractAsync(string templateContent, Rental rental, IEnumerable<RentalItem> items, Customer customer, IEnumerable<Product> products, CompanyInfo? companyInfo = null, CancellationToken ct = default)
            => Task.FromResult(System.Text.Encoding.UTF8.GetBytes("PDF"));
        public Task<string> GenerateAndSaveRentalContractAsync(Rental rental, IEnumerable<RentalItem> items, Customer customer, IEnumerable<Product> products, CompanyInfo? companyInfo = null, CancellationToken ct = default)
            => Task.FromResult($"https://test/contracts/{rental.TenantId}/contract_{rental.Id}.pdf");
        public Task SendRentalContractByEmailAsync(Rental rental, IEnumerable<RentalItem> items, Customer customer, IEnumerable<Product> products, CompanyInfo? companyInfo = null, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task SendRentalConfirmationEmailAsync(Rental rental, IEnumerable<RentalItem> items, Customer customer, IEnumerable<Product> products, CompanyInfo? companyInfo = null, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class FakeFileStorage : SportRental.Admin.Services.Storage.IFileStorage
    {
        public Task<string> SaveAsync(string relativePath, byte[] content, CancellationToken ct = default)
            => Task.FromResult($"https://test/{relativePath}");
        public Task<string> SaveAsync(string relativePath, Stream content, CancellationToken ct = default)
            => Task.FromResult($"https://test/{relativePath}");
        public Task<byte[]> ReadAsync(string relativePath, CancellationToken ct = default)
            => Task.FromResult(Array.Empty<byte>());
        public Task<bool> ExistsAsync(string relativePath, CancellationToken ct = default)
            => Task.FromResult(true);
    }

    private sealed class FakeSmsSender : SportRental.Admin.Services.Sms.ISmsSender
    {
        public Task SendAsync(string phoneNumber, string message, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendThanksMessageAsync(string phoneNumber, string customerName, string? customMessage = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendReminderAsync(string phoneNumber, string customerName, string? customMessage = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendConfirmationRequestAsync(string phoneNumber, string customerName, Guid rentalId, CancellationToken ct = default) => Task.CompletedTask;

        public Task SendContractConfirmationRequestAsync(string phoneNumber, string customerName, Guid rentalId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SendContractConfirmationRequestAsync(string phoneNumber, string customerName, Guid rentalId, string? customerEmail, CancellationToken ct = default)
            => Task.CompletedTask;
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

    private sealed class TestDbContextFactory : IDbContextFactory<ApplicationDbContext>
    {
        private readonly IServiceProvider _serviceProvider;

        public TestDbContextFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public ApplicationDbContext CreateDbContext()
        {
            return _serviceProvider.GetRequiredService<ApplicationDbContext>();
        }

        public ValueTask<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => new(CreateDbContext());
    }
}









