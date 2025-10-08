using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using SportRental.Admin;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;
using SportRental.Infrastructure.Tenancy;
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
using Microsoft.Extensions.Options;namespace SportRental.Admin.Tests;

public sealed class ApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string TestAuthScheme = "TestAuth";
    private const string AuthHeaderName = "X-Test-Auth";
    private static readonly Guid TestTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly WebApplicationFactory<Program> _factory;

    public ApiTests(WebApplicationFactory<Program> factory)
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
                .UseInMemoryDatabase("api-tests")
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
    public async Task GetProducts_ReturnsEmptyByDefault()
    {
        var client = await CreateClientAsync();

        var res = await client.GetAsync("/api/products");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var items = await res.Content.ReadFromJsonAsync<List<object>>();
        Assert.NotNull(items);
    }

    [Fact]
    public async Task Rentals_Post_HappyPath_ReturnsCreated()
    {
        var client = await CreateClientAsync();

        Guid productId;
        Guid customerId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            productId = Guid.NewGuid();
            customerId = Guid.NewGuid();
            await db.Products.AddAsync(new Product { Id = productId, TenantId = TestTenantId, Name = "Narty", Sku = "N-1", DailyPrice = 10, AvailableQuantity = 5, CreatedAtUtc = DateTime.UtcNow });
            await db.Customers.AddAsync(new Customer { Id = customerId, TenantId = TestTenantId, FullName = "Jan" });
            await db.SaveChangesAsync();
        }

        var req = new
        {
            CustomerId = customerId,
            StartDateUtc = DateTime.UtcNow.Date,
            EndDateUtc = DateTime.UtcNow.Date.AddDays(2),
            Items = new[] { new { ProductId = productId, Quantity = 2 } }
        };

        var res = await client.PostAsJsonAsync("/api/rentals", req);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync());
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("contractUrl", out _), "Brak contractUrl w odpowiedzi");
    }

    [Fact]
    public async Task Rentals_Post_Conflict_WhenInsufficientAvailability()
    {
        var client = await CreateClientAsync();
        Guid productId;
        Guid customerId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            productId = Guid.NewGuid();
            customerId = Guid.NewGuid();
            await db.Products.AddAsync(new Product { Id = productId, TenantId = TestTenantId, Name = "Kask", Sku = "K-1", DailyPrice = 10, AvailableQuantity = 1, CreatedAtUtc = DateTime.UtcNow });
            await db.Customers.AddAsync(new Customer { Id = customerId, TenantId = TestTenantId, FullName = "Ewa" });
            var existing = new Rental { Id = Guid.NewGuid(), TenantId = TestTenantId, CustomerId = customerId, StartDateUtc = DateTime.UtcNow.Date, EndDateUtc = DateTime.UtcNow.Date.AddDays(2), Status = RentalStatus.Confirmed, CreatedAtUtc = DateTime.UtcNow };
            await db.Rentals.AddAsync(existing);
            await db.RentalItems.AddAsync(new RentalItem { Id = Guid.NewGuid(), RentalId = existing.Id, ProductId = productId, Quantity = 1, PricePerDay = 10, Subtotal = 10 });
            await db.SaveChangesAsync();
        }

        var req = new
        {
            CustomerId = customerId,
            StartDateUtc = DateTime.UtcNow.Date.AddDays(1),
            EndDateUtc = DateTime.UtcNow.Date.AddDays(2),
            Items = new[] { new { ProductId = productId, Quantity = 1 } }
        };

        var res = await client.PostAsJsonAsync("/api/rentals", req);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task Rentals_Post_BadRequest_OnInvalidDatesAndDuplicates()
    {
        var client = await CreateClientAsync();

        Guid productId;
        Guid customerId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            productId = Guid.NewGuid();
            customerId = Guid.NewGuid();
            await db.Products.AddAsync(new Product { Id = productId, TenantId = TestTenantId, Name = "Buty", Sku = "B-1", DailyPrice = 10, AvailableQuantity = 10, CreatedAtUtc = DateTime.UtcNow });
            await db.Customers.AddAsync(new Customer { Id = customerId, TenantId = TestTenantId, FullName = "Ola" });
            await db.SaveChangesAsync();
        }

        var badDatesReq = new
        {
            CustomerId = customerId,
            StartDateUtc = DateTime.UtcNow.Date,
            EndDateUtc = DateTime.UtcNow.Date,
            Items = new[] { new { ProductId = productId, Quantity = 1 } }
        };
        var res1 = await client.PostAsJsonAsync("/api/rentals", badDatesReq);
        Assert.Equal(HttpStatusCode.BadRequest, res1.StatusCode);

        var dupReq = new
        {
            CustomerId = customerId,
            StartDateUtc = DateTime.UtcNow.Date,
            EndDateUtc = DateTime.UtcNow.Date.AddDays(1),
            Items = new[] { new { ProductId = productId, Quantity = 1 }, new { ProductId = productId, Quantity = 2 } }
        };
        var res2 = await client.PostAsJsonAsync("/api/rentals", dupReq);
        Assert.Equal(HttpStatusCode.BadRequest, res2.StatusCode);
    }

    [Fact]
    public async Task Products_Get_Pagination_Works()
    {
        var client = await CreateClientAsync();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            for (int i = 0; i < 35; i++)
            {
                await db.Products.AddAsync(new Product { Id = Guid.NewGuid(), TenantId = TestTenantId, Name = $"Prod-{i:00}", Sku = $"S-{i:00}", DailyPrice = 1 + i, AvailableQuantity = 10, CreatedAtUtc = DateTime.UtcNow });
            }
            await db.SaveChangesAsync();
        }

        var res = await client.GetAsync("/api/products?page=2&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var list = await res.Content.ReadFromJsonAsync<List<Dictionary<string, object>>>();
        Assert.NotNull(list);
        Assert.Equal(10, list!.Count);
    }

    [Fact]
    public async Task Rentals_Post_Concurrent_OneConflicts()
    {
        var client = await CreateClientAsync();

        Guid productId;
        Guid customerId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            productId = Guid.NewGuid();
            customerId = Guid.NewGuid();
            await db.Products.AddAsync(new Product { Id = productId, TenantId = TestTenantId, Name = "Deska", Sku = "D-1", DailyPrice = 10, AvailableQuantity = 1, CreatedAtUtc = DateTime.UtcNow });
            await db.Customers.AddAsync(new Customer { Id = customerId, TenantId = TestTenantId, FullName = "Konrad" });
            await db.SaveChangesAsync();
        }

        var req = new
        {
            CustomerId = customerId,
            StartDateUtc = DateTime.UtcNow.Date,
            EndDateUtc = DateTime.UtcNow.Date.AddDays(1),
            Items = new[] { new { ProductId = productId, Quantity = 1 } }
        };

        var t1 = client.PostAsJsonAsync("/api/rentals", req);
        var t2 = client.PostAsJsonAsync("/api/rentals", req);
        await Task.WhenAll(t1, t2);

        var statuses = new[] { t1.Result.StatusCode, t2.Result.StatusCode };
        Assert.Contains(HttpStatusCode.Created, statuses);
        Assert.True(statuses.Contains(HttpStatusCode.Conflict) || statuses.All(status => status == HttpStatusCode.Created));
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

    private sealed class TestDbContextFactory : IDbContextFactory<ApplicationDbContext>
    {
        private readonly IServiceProvider _sp;
        public TestDbContextFactory(IServiceProvider sp) => _sp = sp;
        public ApplicationDbContext CreateDbContext()
            => _sp.GetRequiredService<ApplicationDbContext>();
        public ValueTask<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => new(CreateDbContext());
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
                new Claim(ClaimTypes.Name, "test"),
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

    private sealed class TestTenantProvider : ITenantProvider
    {
        private readonly Guid _tenantId;

        public TestTenantProvider(Guid tenantId)
        {
            _tenantId = tenantId;
        }

        public Guid? GetCurrentTenantId() => _tenantId;
    }

    private sealed class FakeContractGenerator : SportRental.Admin.Services.Contracts.IContractGenerator
    {
        public Task<byte[]> GenerateRentalContractAsync(Rental rental, IEnumerable<RentalItem> items, Customer customer, IEnumerable<Product> products, CancellationToken ct = default)
            => Task.FromResult(System.Text.Encoding.UTF8.GetBytes("PDF"));
        public Task<byte[]> GenerateRentalContractAsync(string templateContent, Rental rental, IEnumerable<RentalItem> items, Customer customer, IEnumerable<Product> products, CancellationToken ct = default)
            => Task.FromResult(System.Text.Encoding.UTF8.GetBytes("PDF"));
        public Task<string> GenerateAndSaveRentalContractAsync(Rental rental, IEnumerable<RentalItem> items, Customer customer, IEnumerable<Product> products, CancellationToken ct = default)
            => Task.FromResult($"https://test/contracts/{rental.TenantId}/contract_{rental.Id}.pdf");
        public Task SendRentalContractByEmailAsync(Rental rental, IEnumerable<RentalItem> items, Customer customer, IEnumerable<Product> products, CancellationToken ct = default)
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
    }
}




