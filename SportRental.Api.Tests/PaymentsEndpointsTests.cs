using System.Collections.Generic;
using System.Net.Http.Json;
using System.Linq;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using SportRental.Infrastructure.Domain;
using SportRental.Infrastructure.Data;
using SportRental.Shared.Models;
using Xunit;

namespace SportRental.Api.Tests;

public class PaymentsEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _databasePath;

    public PaymentsEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"payments-tests-{Guid.NewGuid():N}.db");
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
    public async Task CheckoutFlow_WithPaymentIntent_Succeeds()
    {
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

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
                Name = "Deska testowa",
                Sku = "SKU-TEST",
                DailyPrice = 120m,
                AvailableQuantity = 10,
                CreatedAtUtc = DateTime.UtcNow
            });

            db.Customers.Add(new Customer
            {
                Id = customerId,
                TenantId = tenantId,
                FullName = "Test Tester",
                Email = "tester@example.com",
                PhoneNumber = "+48123123123",
                Address = "Testowa 1",
                DocumentNumber = "DOC123",
                Notes = "integration"
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

        using var publicClient = _factory.CreateClient();

        var catalogResponse = await publicClient.GetAsync("/api/products");
        catalogResponse.EnsureSuccessStatusCode();
        var catalog = await catalogResponse.Content.ReadFromJsonAsync<List<ProductDto>>();
        catalog.Should().NotBeNull();
        catalog!.Should().ContainSingle(p => p.Id == productId && p.TenantId == tenantId);

        var productResponse = await publicClient.GetAsync($"/api/products/{productId}");
        productResponse.EnsureSuccessStatusCode();
        var productDetails = await productResponse.Content.ReadFromJsonAsync<ProductDto>();
        productDetails.Should().NotBeNull();
        productDetails!.Id.Should().Be(productId);
        productDetails.TenantId.Should().Be(tenantId);

        using var client = _factory.CreateClient();
        TestApiClientHelper.AuthenticateClient(client, tenantId);

        var start = new DateTime(2025, 1, 10, 9, 0, 0, DateTimeKind.Utc);
        var end = start.AddDays(3);
        var items = new List<CreateRentalItem>
        {
            new CreateRentalItem { ProductId = productId, Quantity = 2 }
        };

        var quoteResponse = await client.PostAsJsonAsync("/api/payments/quote", new PaymentQuoteRequest
        {
            StartDateUtc = start,
            EndDateUtc = end,
            Items = items
        });

        quoteResponse.EnsureSuccessStatusCode();
        var quote = await quoteResponse.Content.ReadFromJsonAsync<PaymentQuoteResponse>();
        quote.Should().NotBeNull();
        quote!.TotalAmount.Should().Be(120m * 2 * 3);
        quote.DepositAmount.Should().Be(Math.Round(quote.TotalAmount * 0.3m, 2, MidpointRounding.AwayFromZero));

        var intentResponse = await client.PostAsJsonAsync("/api/payments/intents", new CreatePaymentIntentRequest
        {
            StartDateUtc = start,
            EndDateUtc = end,
            Items = items
        });

        intentResponse.EnsureSuccessStatusCode();
        var intent = await intentResponse.Content.ReadFromJsonAsync<PaymentIntentDto>();
        intent.Should().NotBeNull();
        // Stripe creates PaymentIntent in "requires_payment_method" state, requires user interaction to complete
        intent!.Status.Should().Be(PaymentIntentStatus.RequiresPaymentMethod);
        intent.Amount.Should().Be(quote.TotalAmount);
        intent.ClientSecret.Should().NotBeNullOrEmpty(); // Needed for frontend Stripe.js

        // Confirm PaymentIntent using Stripe sandbox test card
        StripeTestHelper.GetStripeOptions();
        await StripeTestHelper.ConfirmPaymentIntentAsync(intent.Id);

        var rentalResponse = await client.PostAsJsonAsync("/api/rentals", new CreateRentalRequest
        {
            CustomerId = customerId,
            StartDateUtc = start,
            EndDateUtc = end,
            Notes = "API test",
            IdempotencyKey = $"test:{Guid.NewGuid():N}",
            Items = items,
            PaymentIntentId = intent.Id
        });

        if (!rentalResponse.IsSuccessStatusCode)
        {
            var errorContent = await rentalResponse.Content.ReadAsStringAsync();
            throw new Exception($"Rental creation failed with {rentalResponse.StatusCode}: {errorContent}");
        }

        rentalResponse.EnsureSuccessStatusCode();
        var rental = await rentalResponse.Content.ReadFromJsonAsync<RentalResponse>();
        rental.Should().NotBeNull();
        rental!.TotalAmount.Should().Be(quote.TotalAmount);
        rental.DepositAmount.Should().Be(quote.DepositAmount);
        rental.PaymentStatus.Should().Be(PaymentIntentStatus.Succeeded);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stored = await verifyDb.Rentals.Include(r => r.Items).FirstOrDefaultAsync(r => r.Id == rental.Id);
        stored.Should().NotBeNull();
        stored!.PaymentIntentId.Should().Be(intent.Id);
        stored.DepositAmount.Should().Be(quote.DepositAmount);
        stored.Items.Should().HaveCount(1);

        var listResponse = await client.GetAsync($"/api/my-rentals?customerId={customerId}");
        listResponse.EnsureSuccessStatusCode();
        var list = await listResponse.Content.ReadFromJsonAsync<List<MyRentalDto>>();
        list.Should().NotBeNull();
        list!.Should().ContainSingle();
        var myRental = list.Single();
        myRental.TotalAmount.Should().Be(quote.TotalAmount);
        myRental.DepositAmount.Should().Be(quote.DepositAmount);
        myRental.PaymentStatus.Should().Be(PaymentIntentStatus.Succeeded);
    }
}



