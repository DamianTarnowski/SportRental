using System.Collections.Generic;
using System.Net.Http.Json;
using System.Linq;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;
using SportRental.Shared.Models;
using Xunit;

namespace SportRental.Api.Tests;

public class ProductCatalogEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _databasePath;

    public ProductCatalogEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"catalog-tests-{Guid.NewGuid():N}.db");
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Test");
            builder.UseSetting("ConnectionStrings:DefaultConnection", $"Data Source={_databasePath}");
            builder.UseSetting("Jwt:SigningKey", "TestSigningKey_12345678901234567890");
            builder.UseSetting("Jwt:Issuer", "SportRentalTests");
            builder.UseSetting("Jwt:Audience", "SportRentalTests");
            builder.ConfigureAppConfiguration((context, config) =>
            {
                var stripe = StripeTestHelper.GetStripeOptions();
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = $"Data Source={_databasePath}",
                    ["Jwt:SigningKey"] = "TestSigningKey_12345678901234567890",
                    ["Jwt:Issuer"] = "SportRentalTests",
                    ["Jwt:Audience"] = "SportRentalTests",
                    ["Stripe:SecretKey"] = stripe.SecretKey,
                    ["Stripe:PublishableKey"] = stripe.PublishableKey,
                    ["Stripe:WebhookSecret"] = stripe.WebhookSecret,
                    ["Stripe:Currency"] = stripe.Currency,
                    ["Stripe:SuccessUrl"] = stripe.SuccessUrl ?? "https://localhost:5014/checkout/success?session_id={CHECKOUT_SESSION_ID}",
                    ["Stripe:CancelUrl"] = stripe.CancelUrl ?? "https://localhost:5014/checkout/cancel"
                });
            });

            builder.ConfigureServices(services =>
            {
                var descriptorsToRemove = services
                    .Where(d => d.ServiceType == typeof(ApplicationDbContext)
                             || d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>)
                             || d.ServiceType == typeof(IDbContextOptionsConfiguration<ApplicationDbContext>))
                    .ToList();

                foreach (var descriptor in descriptorsToRemove)
                {
                    services.Remove(descriptor);
                }

                services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite($"Data Source={_databasePath}"));
            });

            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
            });
        });
    }

    private record CatalogSeed(Guid TenantA, Guid TenantB, Guid ProductA, Guid ProductB);

    private async Task<CatalogSeed> SeedCatalogAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var productA = Guid.NewGuid();
        var productB = Guid.NewGuid();

        db.Tenants.AddRange(
            new Tenant { Id = tenantA, Name = "North Rentals", CreatedAtUtc = DateTime.UtcNow },
            new Tenant { Id = tenantB, Name = "South Rentals", CreatedAtUtc = DateTime.UtcNow });

        db.Products.AddRange(
            new Product
            {
                Id = productA,
                TenantId = tenantA,
                Name = "Narty Blizzard",
                DailyPrice = 150m,
                AvailableQuantity = 5,
                CreatedAtUtc = DateTime.UtcNow
            },
            new Product
            {
                Id = productB,
                TenantId = tenantB,
                Name = "Deska Burton",
                DailyPrice = 200m,
                AvailableQuantity = 3,
                CreatedAtUtc = DateTime.UtcNow
            });

        await db.SaveChangesAsync();
        return new CatalogSeed(tenantA, tenantB, productA, productB);
    }

    [Fact]
    public async Task GetProducts_WithoutTenantHeader_ReturnsAllProductsWithTenantInfo()
    {
        var seed = await SeedCatalogAsync();
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/products");
        response.EnsureSuccessStatusCode();

        var products = await response.Content.ReadFromJsonAsync<List<ProductDto>>();
        products.Should().NotBeNull();
        products!.Should().HaveCount(2);
        products.Select(p => p.TenantId).Should().BeEquivalentTo(new[] { seed.TenantA, seed.TenantB });
    }

    [Fact]
    public async Task GetProducts_WithTenantHeader_FiltersCatalog()
    {
        var seed = await SeedCatalogAsync();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", seed.TenantA.ToString());

        var response = await client.GetAsync("/api/products");
        response.EnsureSuccessStatusCode();

        var products = await response.Content.ReadFromJsonAsync<List<ProductDto>>();
        products.Should().NotBeNull();
        products!.Should().HaveCount(1);
        products.Single().TenantId.Should().Be(seed.TenantA);
        products.Single().Name.Should().Contain("Narty");
    }

    [Fact]
    public async Task GetProductById_WithoutTenantHeader_ReturnsProductFromAnyTenant()
    {
        var seed = await SeedCatalogAsync();
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/products/{seed.ProductB}");
        response.EnsureSuccessStatusCode();

        var product = await response.Content.ReadFromJsonAsync<ProductDto>();
        product.Should().NotBeNull();
        product!.Id.Should().Be(seed.ProductB);
        product.TenantId.Should().Be(seed.TenantB);
        product.Name.Should().Be("Deska Burton");
    }
}
