using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SportRental.Api.Payments;
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

                // Replace real Stripe with Mock for tests
                var stripeDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IPaymentGateway));
                if (stripeDescriptor != null)
                {
                    services.Remove(stripeDescriptor);
                }
                services.AddSingleton<IPaymentGateway, MockPaymentGateway>();
            });
        });
    }

    [Fact]
    public async Task CreateCheckoutSession_ValidRequest_ReturnsSessionUrl()
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

            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId.ToString());

        var request = new CreateCheckoutSessionRequest(
            StartDateUtc: DateTime.UtcNow.AddDays(1),
            EndDateUtc: DateTime.UtcNow.AddDays(3),
            Items: new List<CheckoutItem> { new(productId, 2) },
            CustomerEmail: "test@example.com",
            CustomerId: Guid.NewGuid()
        );

        // Act
        var response = await client.PostAsJsonAsync("/api/checkout/create-session", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var result = await response.Content.ReadFromJsonAsync<CheckoutSessionResponse>();
        result.Should().NotBeNull();
        result!.SessionId.Should().NotBeEmpty();
        result.Url.Should().NotBeEmpty();
        result.Url.Should().StartWith("https://checkout.stripe.com/");
        result.ExpiresAt.Should().BeAfter(DateTime.UtcNow);
    }

    [Fact]
    public async Task CreateCheckoutSession_EmptyItems_ReturnsBadRequest()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", Guid.NewGuid().ToString());

        var request = new CreateCheckoutSessionRequest(
            StartDateUtc: DateTime.UtcNow.AddDays(1),
            EndDateUtc: DateTime.UtcNow.AddDays(3),
            Items: new List<CheckoutItem>(), // Empty!
            CustomerEmail: "test@example.com"
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

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId.ToString());

        var request = new CreateCheckoutSessionRequest(
            StartDateUtc: DateTime.UtcNow.AddDays(5), // End before start!
            EndDateUtc: DateTime.UtcNow.AddDays(3),
            Items: new List<CheckoutItem> { new(productId, 1) },
            CustomerEmail: "test@example.com"
        );

        // Act
        var response = await client.PostAsJsonAsync("/api/checkout/create-session", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateCheckoutSession_NoTenantId_ReturnsBadRequest()
    {
        // Arrange
        var client = _factory.CreateClient();
        // No X-Tenant-Id header!

        var request = new CreateCheckoutSessionRequest(
            StartDateUtc: DateTime.UtcNow.AddDays(1),
            EndDateUtc: DateTime.UtcNow.AddDays(3),
            Items: new List<CheckoutItem> { new(Guid.NewGuid(), 1) },
            CustomerEmail: "test@example.com"
        );

        // Act
        var response = await client.PostAsJsonAsync("/api/checkout/create-session", request);

        // Assert
        // Auth endpoints are exempt, but checkout is not
        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized);
    }
}