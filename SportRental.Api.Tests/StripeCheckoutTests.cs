using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;
using SportRental.Shared.Models;
using Xunit;

namespace SportRental.Api.Tests;

public class StripeCheckoutTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _databasePath;

    public StripeCheckoutTests(WebApplicationFactory<Program> factory)
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"checkout-tests-{Guid.NewGuid():N}.db");
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
                // Remove all EF Core related services (including DbContextPool)
                var descriptorsToRemove = services
                    .Where(d => d.ServiceType == typeof(ApplicationDbContext) 
                             || d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>)
                             || d.ServiceType == typeof(IDbContextOptionsConfiguration<ApplicationDbContext>)
                             || d.ServiceType == typeof(DbContextOptions)
                             || d.ServiceType.IsGenericType && d.ServiceType.GetGenericTypeDefinition().Name.Contains("DbContext"))
                    .ToList();
                
                foreach (var descriptor in descriptorsToRemove)
                {
                    services.Remove(descriptor);
                }

                // Add SQLite DbContext for testing (not pooled)
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

    [Fact]
    public async Task CreateCheckoutSession_ValidRequest_ReturnsSessionUrl()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        // Seed database
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.EnsureDeletedAsync();
            await db.Database.EnsureCreatedAsync();

            // Add Tenant first (required for foreign keys)
            db.Tenants.Add(new Tenant
            {
                Id = tenantId,
                Name = "Test Rental",
                CreatedAtUtc = DateTime.UtcNow
            });

            db.Products.Add(new Product
            {
                Id = productId,
                TenantId = tenantId,
                Name = "Test Narty",
                Sku = "SKU-TEST-1",
                DailyPrice = 100m,
                AvailableQuantity = 10,
                Type = 1,
                CreatedAtUtc = DateTime.UtcNow
            });

            // Add CompanyInfo for PDF contract generation
            db.CompanyInfos.Add(new CompanyInfo
            {
                TenantId = tenantId,
                Name = "Test Rental Company",
                NIP = "1234567890",
                Address = "Test Street 123, 00-000 Test City",
                Email = "contact@testrental.com",
                PhoneNumber = "+48123456789"
            });

            db.Customers.Add(new Customer
            {
                Id = customerId,
                TenantId = tenantId,
                FullName = "Checkout Tester",
                Email = "test@example.com",
                CreatedAtUtc = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
        }

        using var client = _factory.CreateClient();
        TestApiClientHelper.AuthenticateClient(client, tenantId);

        var request = new CreateCheckoutSessionRequest(
            StartDateUtc: DateTime.UtcNow.AddDays(1),
            EndDateUtc: DateTime.UtcNow.AddDays(3),
            Items: new List<CheckoutItem> { new(productId, 2) },
            CustomerEmail: "test@example.com",
            CustomerId: customerId
        );

        // Act
        var response = await client.PostAsJsonAsync("/api/checkout/create-session", request);
        var responseBody = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {responseBody}");
        
        var result = await response.Content.ReadFromJsonAsync<CheckoutSessionResponse>();
        result.Should().NotBeNull();
        result!.SessionId.Should().NotBeEmpty();
        result.Url.Should().NotBeNullOrWhiteSpace();
        result.Url.Should().StartWith("https://checkout.stripe.com/");
        result.ExpiresAt.Should().BeAfter(DateTime.UtcNow);
    }

    [Fact]
    public async Task CreateCheckoutSession_EmptyItems_ReturnsBadRequest()
    {
        // Arrange
        using var client = _factory.CreateClient();
        TestApiClientHelper.AuthenticateClient(client, Guid.NewGuid());

        var request = new CreateCheckoutSessionRequest(
            StartDateUtc: DateTime.UtcNow.AddDays(1),
            EndDateUtc: DateTime.UtcNow.AddDays(3),
            Items: new List<CheckoutItem>(), // Empty!
            CustomerEmail: "test@example.com",
            CustomerId: Guid.NewGuid()
        );

        // Act
        var response = await client.PostAsJsonAsync("/api/checkout/create-session", request);

        // Assert
        // Now we validate empty items - should return 400
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateCheckoutSession_InvalidDates_ReturnsBadRequest()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        // Seed database
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.EnsureDeletedAsync();
            await db.Database.EnsureCreatedAsync();

            // Add Tenant first (required for foreign keys)
            db.Tenants.Add(new Tenant
            {
                Id = tenantId,
                Name = "Test Rental",
                CreatedAtUtc = DateTime.UtcNow
            });

            db.Products.Add(new Product
            {
                Id = productId,
                TenantId = tenantId,
                Name = "Test Narty",
                Sku = "SKU-TEST-2",
                DailyPrice = 100m,
                AvailableQuantity = 10,
                Type = 1,
                CreatedAtUtc = DateTime.UtcNow
            });

            // Add CompanyInfo for PDF contract generation
            db.CompanyInfos.Add(new CompanyInfo
            {
                TenantId = tenantId,
                Name = "Test Rental Company",
                NIP = "1234567890",
                Address = "Test Street 123, 00-000 Test City",
                Email = "contact@testrental.com",
                PhoneNumber = "+48123456789"
            });

            await db.SaveChangesAsync();
        }

        using var client = _factory.CreateClient();
        TestApiClientHelper.AuthenticateClient(client, tenantId);

        var request = new CreateCheckoutSessionRequest(
            StartDateUtc: DateTime.UtcNow.AddDays(5), // End before start!
            EndDateUtc: DateTime.UtcNow.AddDays(3),
            Items: new List<CheckoutItem> { new(productId, 1) },
            CustomerEmail: "test@example.com",
            CustomerId: Guid.NewGuid()
        );

        // Act
        var response = await client.PostAsJsonAsync("/api/checkout/create-session", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateCheckoutSession_MixedTenants_ReturnsSessionUrl()
    {
        // Arrange
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var productA = Guid.NewGuid();
        var productB = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.EnsureDeletedAsync();
            await db.Database.EnsureCreatedAsync();

            db.Tenants.AddRange(
                new Tenant { Id = tenantA, Name = "Rental A", CreatedAtUtc = DateTime.UtcNow },
                new Tenant { Id = tenantB, Name = "Rental B", CreatedAtUtc = DateTime.UtcNow });

            db.Products.AddRange(
                new Product { Id = productA, TenantId = tenantA, Name = "Narty A", Sku = "SKU-A", DailyPrice = 50m, AvailableQuantity = 5, Type = 1, CreatedAtUtc = DateTime.UtcNow },
                new Product { Id = productB, TenantId = tenantB, Name = "Deska B", Sku = "SKU-B", DailyPrice = 80m, AvailableQuantity = 5, Type = 1, CreatedAtUtc = DateTime.UtcNow });

            db.Customers.Add(new Customer
            {
                Id = customerId,
                TenantId = tenantA,
                FullName = "Multi Tenant Klient",
                Email = "multi@test.pl",
                CreatedAtUtc = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
        }

        using var client = _factory.CreateClient();
        var request = new CreateCheckoutSessionRequest(
            StartDateUtc: DateTime.UtcNow.AddDays(1),
            EndDateUtc: DateTime.UtcNow.AddDays(2),
            Items: new List<CheckoutItem>
            {
                new(productA, 1),
                new(productB, 2)
            },
            CustomerEmail: "multi@test.pl",
            CustomerId: customerId
        );

        // Act
        var response = await client.PostAsJsonAsync("/api/checkout/create-session", request);
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response: {body}");
        var result = await response.Content.ReadFromJsonAsync<CheckoutSessionResponse>();
        result.Should().NotBeNull();
        result!.SessionId.Should().NotBeNullOrWhiteSpace();
        result.Url.Should().StartWith("https://checkout.stripe.com/");
    }
}
