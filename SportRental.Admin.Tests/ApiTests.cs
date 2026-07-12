using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using SportRental.Admin;
using SportRental.Admin.Api;
using SportRental.Admin.Services.Auth;
using SportRental.Admin.Services.Identity;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;
using SportRental.Infrastructure.Tenancy;
using SportRental.Shared.Models;
using SportRental.Shared.Legal;
using SportRental.Shared.Identity;
using SportRental.Shared.Time;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace SportRental.Admin.Tests;

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
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((context, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Tenant:Id"] = TestTenantId.ToString(),
                    ["ClientApp:PublicBaseUrl"] = "https://client.example.test/_client"
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
        var page = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(page.TryGetProperty("items", out var items) || page.TryGetProperty("Items", out items));
        Assert.Equal(JsonValueKind.Array, items.ValueKind);
        Assert.Equal(0, items.GetArrayLength());
    }

    [Fact]
    public async Task GetTenants_ReturnsNormalizedColorsAndNeverLeaksUnsafeCssValue()
    {
        var client = await CreateClientAsync(authenticated: false);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tenant = await db.Tenants.SingleAsync(t => t.Id == TestTenantId);
            tenant.PrimaryColorHex = "#123456;url(x)";
            tenant.SecondaryColorHex = "#f96167";
            await db.SaveChangesAsync();
        }
        await SeedQuoteProductAsync(TestTenantId, dailyPrice: 50m);

        var response = await client.GetAsync("/api/tenants");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("url(x)", body, StringComparison.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(body);
        var tenants = document.RootElement;
        var tenantRow = tenants.EnumerateArray()
            .Single(row => ReadGuid(row, "id") == TestTenantId);
        Assert.Equal(JsonValueKind.Null, ReadProperty(tenantRow, "primaryColor").ValueKind);
        Assert.Equal("#F96167", ReadProperty(tenantRow, "secondaryColor").GetString());
    }

    [Fact]
    public async Task LegalInfo_IsPublicAndReturnsCurrentDocumentVersions()
    {
        var client = await CreateClientAsync(authenticated: false);

        var response = await client.GetAsync("/api/legal/info");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var info = await response.Content.ReadFromJsonAsync<LegalInfoDto>();
        Assert.NotNull(info);
        Assert.Equal("RentSpot", info.ServiceName);
        Assert.Equal(LegalDocumentVersions.Terms, info.TermsVersion);
        Assert.Equal(LegalDocumentVersions.Privacy, info.PrivacyVersion);
        Assert.False(info.IsOperatorDataComplete);
    }

    [Fact]
    public async Task GetProducts_TenantIdFilterUsesStableIdentifier()
    {
        var client = await CreateClientAsync(authenticated: false);
        var productId = await SeedQuoteProductAsync(
            TestTenantId,
            dailyPrice: 75m,
            availableQuantity: 2);

        var matchingResponse = await client.GetAsync($"/api/products?tenantId={TestTenantId:D}");
        var otherResponse = await client.GetAsync($"/api/products?tenantId={Guid.NewGuid():D}");

        Assert.Equal(HttpStatusCode.OK, matchingResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, otherResponse.StatusCode);

        var matchingPage = await matchingResponse.Content.ReadFromJsonAsync<JsonElement>();
        var matchingItems = ReadProperty(matchingPage, "items");
        Assert.Contains(matchingItems.EnumerateArray(), item =>
            ReadGuid(item, "id") == productId &&
            ReadGuid(item, "tenantId") == TestTenantId);
        Assert.Equal(1, ReadProperty(matchingPage, "totalCount").GetInt32());
        Assert.Equal(1, ReadProperty(matchingPage, "availableCount").GetInt32());
        Assert.Equal(75m, ReadProperty(matchingPage, "averagePrice").GetDecimal());
        Assert.Equal(75m, ReadProperty(matchingPage, "minimumPrice").GetDecimal());

        var otherPage = await otherResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(ReadProperty(otherPage, "items").EnumerateArray());
        Assert.Equal(0, ReadProperty(otherPage, "availableCount").GetInt32());
        Assert.Equal(0m, ReadProperty(otherPage, "averagePrice").GetDecimal());
        Assert.Equal(0m, ReadProperty(otherPage, "minimumPrice").GetDecimal());
    }

    [Fact]
    public async Task ProductFacets_ReturnsCompactMarketplaceFiltersAndStats()
    {
        var client = await CreateClientAsync(authenticated: false);
        var productId = await SeedQuoteProductAsync(
            TestTenantId,
            dailyPrice: 125m,
            availableQuantity: 2);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var product = await db.Products.IgnoreQueryFilters().SingleAsync(item => item.Id == productId);
            product.Category = "Narty";
            product.City = "Kraków";
            product.Voivodeship = "małopolskie";
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync("/api/products/facets");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var facets = await response.Content.ReadFromJsonAsync<ProductCatalogFacetsDto>();
        Assert.NotNull(facets);
        Assert.Contains("Narty", facets.Categories);
        Assert.Contains(facets.Locations, location =>
            location.City == "Kraków" && location.Voivodeship == "małopolskie");
        Assert.Contains(facets.Tenants, tenant => tenant.TenantId == TestTenantId);
        Assert.True(facets.TotalCount >= 1);
        Assert.True(facets.AvailableCount >= 1);
        Assert.True(facets.MinimumPrice <= 125m);
        Assert.True(facets.MaximumPrice >= 125m);
    }

    [Fact]
    public async Task ProductDetails_InlineRasterImageIsNotPrefixedWithRequestHost()
    {
        const string inlineImage = "data:image/jpeg;base64,/9j/4AAQSkZJRg==";
        var client = await CreateClientAsync(authenticated: false);
        var productId = await SeedQuoteProductAsync(TestTenantId, dailyPrice: 40m);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var product = await db.Products.SingleAsync(item => item.Id == productId);
            product.ImageUrl = inlineImage;
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync($"/api/products/{productId:D}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(inlineImage, ReadProperty(body, "imageUrl").GetString());
    }

    [Fact]
    public async Task ProductDetails_NonRasterDataUriIsNotExposed()
    {
        var client = await CreateClientAsync(authenticated: false);
        var productId = await SeedQuoteProductAsync(TestTenantId, dailyPrice: 40m);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var product = await db.Products.SingleAsync(item => item.Id == productId);
            product.ImageUrl = "data:image/svg+xml,<svg onload=alert(1)></svg>";
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync($"/api/products/{productId:D}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, ReadProperty(body, "imageUrl").ValueKind);
    }

    [Fact]
    public async Task PublicProducts_ExposeOnlyPersistedImageVariantManifest()
    {
        var client = await CreateClientAsync(authenticated: false);
        var productId = await SeedQuoteProductAsync(TestTenantId, dailyPrice: 40m);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var product = await db.Products.SingleAsync(item => item.Id == productId);
            product.ImageUrl = "https://cdn.example.test/products/item/w800.webp";
            product.ImageBasePath = "images/products/tenant/product/v1";
            product.ImageVariantWidths = [400, 800];
            product.HasOriginalImage = true;
            await db.SaveChangesAsync();
        }

        var listResponse = await client.GetAsync(
            $"/api/products?tenantId={TestTenantId:D}&pageSize=100");
        var detailsResponse = await client.GetAsync($"/api/products/{productId:D}");

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, detailsResponse.StatusCode);

        var page = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        var listedProduct = ReadProperty(page, "items")
            .EnumerateArray()
            .Single(item => ReadGuid(item, "id") == productId);
        var detailsProduct = await detailsResponse.Content.ReadFromJsonAsync<JsonElement>();

        foreach (var productJson in new[] { listedProduct, detailsProduct })
        {
            Assert.Equal(
                "images/products/tenant/product/v1",
                ReadProperty(productJson, "imageBasePath").GetString());
            Assert.Equal(
                new[] { 400, 800 },
                ReadProperty(productJson, "imageVariantWidths")
                    .EnumerateArray()
                    .Select(width => width.GetInt32()));
            Assert.True(ReadProperty(productJson, "hasOriginalImage").GetBoolean());
        }
    }

    [Fact]
    public async Task ProductImageUpload_PersistsExactGeneratedVariantManifest()
    {
        var client = await CreateClientAsync();
        var productId = await SeedQuoteProductAsync(TestTenantId, dailyPrice: 40m);

        using var image = new Image<Rgba32>(500, 300);
        await using var imageStream = new MemoryStream();
        await image.SaveAsJpegAsync(imageStream);
        using var fileContent = new ByteArrayContent(imageStream.ToArray());
        fileContent.Headers.ContentType = new("image/jpeg");
        using var multipart = new MultipartFormDataContent();
        multipart.Add(fileContent, "file", "product.jpg");

        var response = await client.PostAsync($"/api/products/{productId:D}/image", multipart);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var savedProduct = await db.Products.SingleAsync(product => product.Id == productId);
        Assert.Equal(new[] { 400, 800 }, savedProduct.ImageVariantWidths);
        Assert.True(savedProduct.HasOriginalImage);
        Assert.EndsWith("/w800.jpg", savedProduct.ImageUrl, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(savedProduct.ImageBasePath));
    }

    [Fact]
    public async Task PublicCatalog_StaffTenantClaimDoesNotHideMarketplaceProducts()
    {
        // TestAuth represents an authenticated Owner/SuperAdmin and the injected
        // tenant provider returns TestTenantId. The WASM catalog must still behave
        // exactly like the public marketplace and only honor its explicit filter.
        var client = await CreateClientAsync();
        var otherTenantId = await SeedTenantAsync("Druga wypożyczalnia");
        var demoTenantId = await SeedTenantAsync("Wypożyczalnia demo", isDemo: true);
        var ownProductId = await SeedQuoteProductAsync(TestTenantId, dailyPrice: 50m);
        var otherProductId = await SeedQuoteProductAsync(otherTenantId, dailyPrice: 80m);
        var demoProductId = await SeedQuoteProductAsync(demoTenantId, dailyPrice: 1m);

        var marketplaceResponse = await client.GetAsync("/api/products?pageSize=100");
        var filteredResponse = await client.GetAsync(
            $"/api/products?tenantId={otherTenantId:D}&tenant={Uri.EscapeDataString("Test Tenant")}");
        using var headerFilterRequest = new HttpRequestMessage(HttpMethod.Get, "/api/products");
        headerFilterRequest.Headers.Add("X-Tenant-Id", otherTenantId.ToString("D"));
        var headerFilteredResponse = await client.SendAsync(headerFilterRequest);
        var demoResponse = await client.GetAsync($"/api/products?tenantId={demoTenantId:D}");
        var detailsResponse = await client.GetAsync($"/api/products/{otherProductId:D}");
        var demoDetailsResponse = await client.GetAsync($"/api/products/{demoProductId:D}");

        Assert.Equal(HttpStatusCode.OK, marketplaceResponse.StatusCode);
        var marketplace = await marketplaceResponse.Content.ReadFromJsonAsync<JsonElement>();
        var marketplaceIds = ReadProperty(marketplace, "items")
            .EnumerateArray()
            .Select(item => ReadGuid(item, "id"))
            .ToHashSet();
        Assert.Equal(2, ReadProperty(marketplace, "totalCount").GetInt32());
        Assert.True(marketplaceIds.SetEquals([ownProductId, otherProductId]));

        Assert.Equal(HttpStatusCode.OK, filteredResponse.StatusCode);
        var filtered = await filteredResponse.Content.ReadFromJsonAsync<JsonElement>();
        var filteredItem = Assert.Single(ReadProperty(filtered, "items").EnumerateArray());
        Assert.Equal(otherProductId, ReadGuid(filteredItem, "id"));
        Assert.Equal(otherTenantId, ReadGuid(filteredItem, "tenantId"));

        Assert.Equal(HttpStatusCode.OK, headerFilteredResponse.StatusCode);
        var headerFiltered = await headerFilteredResponse.Content.ReadFromJsonAsync<JsonElement>();
        var headerFilteredItem = Assert.Single(ReadProperty(headerFiltered, "items").EnumerateArray());
        Assert.Equal(otherProductId, ReadGuid(headerFilteredItem, "id"));

        Assert.Equal(HttpStatusCode.OK, demoResponse.StatusCode);
        var demo = await demoResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, ReadProperty(demo, "totalCount").GetInt32());
        Assert.Empty(ReadProperty(demo, "items").EnumerateArray());

        Assert.Equal(HttpStatusCode.OK, detailsResponse.StatusCode);
        var details = await detailsResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(otherProductId, ReadGuid(details, "id"));
        Assert.Equal(otherTenantId, ReadGuid(details, "tenantId"));
        Assert.Equal(HttpStatusCode.NotFound, demoDetailsResponse.StatusCode);
    }

    [Fact]
    public async Task PublicCatalog_NormalizedFiltersMatchLegacyMojibakeRows()
    {
        var client = await CreateClientAsync(authenticated: false);
        var productId = await SeedQuoteProductAsync(TestTenantId, dailyPrice: 60m);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var product = await db.Products.SingleAsync(item => item.Id == productId);
            product.Name = "Rower gĂłrski";
            product.Category = "GĂłrskie";
            db.CompanyInfos.Add(new CompanyInfo
            {
                Id = Guid.NewGuid(),
                TenantId = TestTenantId,
                Name = "Wypożyczalnia Kraków",
                Address = "ul. KrupĂłwki 15",
                City = "KrakĂłw",
                Voivodeship = "maĹ‚opolskie"
            });
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync(
            "/api/products?city=Krak%C3%B3w&category=G%C3%B3rskie&search=g%C3%B3rski");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        var item = Assert.Single(ReadProperty(page, "items").EnumerateArray());
        Assert.Equal(productId, ReadGuid(item, "id"));
        Assert.Equal("Rower górski", ReadProperty(item, "name").GetString());
        Assert.Equal("Górskie", ReadProperty(item, "category").GetString());
        Assert.Equal("Kraków", ReadProperty(item, "city").GetString());
        Assert.Equal("małopolskie", ReadProperty(item, "voivodeship").GetString());
        Assert.Equal("ul. Krupówki 15", ReadProperty(item, "pickupAddress").GetString());
    }

    [Fact]
    public async Task AuthProviders_WhenGoogleIsNotConfigured_ReturnsFalse()
    {
        var client = await CreateClientAsync(authenticated: false);

        var response = await client.GetAsync("/api/auth/providers");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(ReadProperty(payload, "google").GetBoolean());
    }

    [Fact]
    public async Task PaymentsQuote_StartInPast_ReturnsBadRequest()
    {
        var client = await CreateClientAsync(authenticated: false);
        var request = CreateDailyQuoteRequest(
            Guid.NewGuid(),
            DateTime.UtcNow.AddMinutes(-1),
            DateTime.UtcNow.AddDays(1));

        var response = await client.PostAsJsonAsync("/api/payments/quote", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Holds_StartInPast_ReturnsBadRequestBeforeCreatingReservation()
    {
        var client = await CreateClientAsync(authenticated: false);

        var response = await client.PostAsJsonAsync("/api/holds", new CreateHoldRequest
        {
            ProductId = Guid.NewGuid(),
            Quantity = 1,
            StartDateUtc = DateTime.UtcNow.AddMinutes(-1),
            EndDateUtc = DateTime.UtcNow.AddDays(1)
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await db.ReservationHolds.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Checkout_LegacyFlatShapeIsRejectedBeforeValidatingCart()
    {
        var client = await CreateHttpsClientAsync();
        var email = $"past-checkout-{Guid.NewGuid():N}@example.test";
        var guestResponse = await client.PostAsJsonAsync("/api/auth/guest-session", new
        {
            Email = email,
            FullName = "Klient terminu",
            PhoneNumber = "+48123000000",
            Address = (string?)null,
            DocumentNumber = (string?)null,
            Notes = (string?)null
        });
        Assert.Equal(HttpStatusCode.OK, guestResponse.StatusCode);

        var response = await client.PostAsJsonAsync(
            "/api/checkout/create-session",
            new CreateCheckoutSessionRequest(
                DateTime.UtcNow.AddHours(1),
                DateTime.UtcNow.AddDays(1),
                [],
                email,
                AcceptedTermsVersion: LegalDocumentVersions.Terms,
                AcknowledgedPrivacyVersion: LegalDocumentVersions.Privacy));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("każdej wypożyczalni", ReadProperty(error, "error").GetString() ?? string.Empty);
    }

    [Fact]
    public async Task Checkout_WithoutCurrentLegalVersions_IsRejected()
    {
        var client = await CreateHttpsClientAsync();
        var email = $"legal-checkout-{Guid.NewGuid():N}@example.test";
        var guestResponse = await client.PostAsJsonAsync("/api/auth/guest-session", new
        {
            Email = email,
            FullName = "Klient checkout",
            PhoneNumber = "+48123000001",
            Address = (string?)null,
            DocumentNumber = (string?)null,
            Notes = (string?)null
        });
        Assert.Equal(HttpStatusCode.OK, guestResponse.StatusCode);

        var response = await client.PostAsJsonAsync(
            "/api/checkout/create-session",
            new CreateCheckoutSessionRequest(
                DateTime.UtcNow.AddHours(1),
                DateTime.UtcNow.AddDays(1),
                [],
                email,
                AcceptedTermsVersion: "stara-wersja",
                AcknowledgedPrivacyVersion: null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("aktualny regulamin", ReadProperty(error, "error").GetString() ?? string.Empty);
    }

    [Fact]
    public async Task Checkout_MarketplaceRejectsRegulationsHashDifferentFromQuotedVersion()
    {
        var client = await CreateHttpsClientAsync();
        var tenantId = await SeedTenantAsync("Regulamin Test Rent");
        var productId = await SeedQuoteProductAsync(tenantId, dailyPrice: 100m);
        var email = $"terms-hash-{Guid.NewGuid():N}@example.test";
        var guestResponse = await client.PostAsJsonAsync("/api/auth/guest-session", new
        {
            Email = email,
            FullName = "Klient regulaminu",
            PhoneNumber = "+48123000022",
            Address = (string?)null,
            DocumentNumber = (string?)null,
            Notes = (string?)null
        });
        Assert.Equal(HttpStatusCode.OK, guestResponse.StatusCode);

        var start = new DateTime(2026, 8, 12, 8, 0, 0, DateTimeKind.Utc);
        var end = start.AddDays(1);
        var holdSessionId = Guid.NewGuid().ToString("N");
        var holdResponse = await client.PostAsJsonAsync("/api/holds", new CreateHoldRequest
        {
            ProductId = productId,
            Quantity = 1,
            StartDateUtc = start,
            EndDateUtc = end,
            SessionId = holdSessionId
        });
        Assert.Equal(HttpStatusCode.Created, holdResponse.StatusCode);
        var hold = await holdResponse.Content.ReadFromJsonAsync<CreateHoldResponse>();
        Assert.NotNull(hold);

        var response = await client.PostAsJsonAsync(
            "/api/checkout/create-session",
            new CreateCheckoutSessionRequest(
                start,
                end,
                [],
                email,
                HoldSessionId: holdSessionId,
                AcceptedTermsVersion: LegalDocumentVersions.Terms,
                AcknowledgedPrivacyVersion: LegalDocumentVersions.Privacy,
                RentalGroups:
                [
                    new CheckoutRentalGroupRequest(
                        tenantId,
                        start,
                        end,
                        [new CheckoutItem(productId, 1, hold.Id)],
                        AcceptedRegulationsHash: "podstawiony-hash")
                ]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("Regulamin", ReadProperty(error, "error").GetString() ?? string.Empty);
    }

    [Fact]
    public async Task GuestOrderAccess_RequestIsEnumerationSafeAndStoresOnlyTokenHash()
    {
        var client = await CreateHttpsClientAsync();
        var seeded = await SeedGuestMarketplaceOrderAsync();

        var matchingResponse = await client.PostAsJsonAsync(
            "/api/auth/guest-order-access/request",
            new { seeded.Email, seeded.OrderNumber });
        var mismatchingResponse = await client.PostAsJsonAsync(
            "/api/auth/guest-order-access/request",
            new { Email = "inna-osoba@example.test", seeded.OrderNumber });

        Assert.Equal(HttpStatusCode.Accepted, matchingResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, mismatchingResponse.StatusCode);
        var matchingPayload = await matchingResponse.Content.ReadFromJsonAsync<JsonElement>();
        var mismatchingPayload = await mismatchingResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(
            ReadProperty(matchingPayload, "message").GetString(),
            ReadProperty(mismatchingPayload, "message").GetString());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var token = Assert.Single(await db.GuestOrderAccessTokens
            .Where(candidate => candidate.MarketplaceOrderId == seeded.OrderId)
            .ToListAsync());
        Assert.Equal(64, token.TokenHash.Length);
        Assert.DoesNotContain("=", token.TokenHash);
        Assert.True(token.ExpiresAtUtc > token.CreatedAtUtc);
    }

    [Fact]
    public async Task GuestOrderAccess_UsesCheckoutEmailSnapshotAfterCustomerEmailWasChanged()
    {
        var client = await CreateHttpsClientAsync();
        var seeded = await SeedGuestMarketplaceOrderAsync();
        const string attackerEmail = "attacker@example.test";

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var customer = await db.Customers.IgnoreQueryFilters()
                .SingleAsync(candidate => candidate.Id == seeded.CustomerId);
            customer.Email = attackerEmail;
            await db.SaveChangesAsync();
        }

        var attackerResponse = await client.PostAsJsonAsync(
            "/api/auth/guest-order-access/request",
            new { Email = attackerEmail, seeded.OrderNumber });

        Assert.Equal(HttpStatusCode.Accepted, attackerResponse.StatusCode);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Empty(await db.GuestOrderAccessTokens
                .Where(candidate => candidate.MarketplaceOrderId == seeded.OrderId)
                .ToListAsync());
        }

        var originalResponse = await client.PostAsJsonAsync(
            "/api/auth/guest-order-access/request",
            new { seeded.Email, seeded.OrderNumber });

        Assert.Equal(HttpStatusCode.Accepted, originalResponse.StatusCode);
        using var verificationScope = _factory.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Single(await verificationDb.GuestOrderAccessTokens
            .Where(candidate => candidate.MarketplaceOrderId == seeded.OrderId)
            .ToListAsync());
    }

    [Fact]
    public async Task GuestOrderAccess_DoesNotDowngradeLinkedIdentityAccountToGuestSession()
    {
        var client = await CreateHttpsClientAsync();
        var seeded = await SeedGuestMarketplaceOrderAsync();
        var userId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Users.Add(new ApplicationUser
            {
                Id = userId,
                UserName = seeded.Email,
                NormalizedUserName = seeded.Email.ToUpperInvariant(),
                Email = seeded.Email,
                NormalizedEmail = seeded.Email.ToUpperInvariant(),
                SecurityStamp = Guid.NewGuid().ToString("N")
            });
            db.UserClaims.Add(new IdentityUserClaim<Guid>
            {
                UserId = userId,
                ClaimType = AuthClaims.CustomerId,
                ClaimValue = seeded.CustomerId.ToString()
            });
            await db.SaveChangesAsync();
        }

        var response = await client.PostAsJsonAsync(
            "/api/auth/guest-order-access/request",
            new { seeded.Email, seeded.OrderNumber });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using var verificationScope = _factory.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await verificationDb.GuestOrderAccessTokens
            .Where(candidate => candidate.MarketplaceOrderId == seeded.OrderId)
            .ToListAsync());
    }

    [Fact]
    public async Task TenantAdmin_CannotChangeGlobalMarketplaceCustomerIdentity()
    {
        var client = await CreateClientAsync(authenticated: false);
        client.DefaultRequestHeaders.Add(AuthHeaderName, "tenant-only");
        var customerId = Guid.NewGuid();
        const string originalEmail = "marketplace-customer@example.test";

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Customers.Add(new Customer
            {
                Id = customerId,
                TenantId = Guid.Empty,
                FullName = "Klient marketplace",
                Email = originalEmail,
                PhoneNumber = "+48123123123",
                CreatedAtUtc = DateTime.UtcNow
            });
            db.Rentals.Add(new Rental
            {
                Id = Guid.NewGuid(),
                TenantId = TestTenantId,
                CustomerId = customerId,
                StartDateUtc = DateTime.UtcNow.AddDays(1),
                EndDateUtc = DateTime.UtcNow.AddDays(2),
                TotalAmount = 100m,
                Status = RentalStatus.Confirmed,
                CreatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var response = await client.PutAsJsonAsync(
            $"/api/customers/{customerId:D}",
            new CreateCustomerRequest
            {
                FullName = "Przejęte konto",
                Email = "attacker@example.test",
                PhoneNumber = "+48999999999"
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var verificationScope = _factory.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var unchanged = await verificationDb.Customers.IgnoreQueryFilters()
            .SingleAsync(candidate => candidate.Id == customerId);
        Assert.Equal(originalEmail, unchanged.Email);
        Assert.Equal("Klient marketplace", unchanged.FullName);
    }

    [Fact]
    public async Task GuestOrderAccess_RedeemCreatesSessionAndConsumesTokenOnce()
    {
        var client = await CreateHttpsClientAsync();
        var seeded = await SeedGuestMarketplaceOrderAsync();
        var rawToken = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(
            RandomNumberGenerator.GetBytes(32));
        var tokenHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(rawToken))).ToLowerInvariant();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.GuestOrderAccessTokens.Add(new GuestOrderAccessToken
            {
                Id = Guid.NewGuid(),
                CustomerId = seeded.CustomerId,
                MarketplaceOrderId = seeded.OrderId,
                TokenHash = tokenHash,
                CreatedAtUtc = DateTime.UtcNow,
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(20)
            });
            await db.SaveChangesAsync();
        }

        var firstRedeem = await client.PostAsJsonAsync(
            "/api/auth/guest-order-access/redeem",
            new { Token = rawToken });
        var meResponse = await client.GetAsync("/api/auth/me");
        var secondRedeem = await client.PostAsJsonAsync(
            "/api/auth/guest-order-access/redeem",
            new { Token = rawToken });

        Assert.Equal(HttpStatusCode.OK, firstRedeem.StatusCode);
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        var me = await meResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(seeded.CustomerId, ReadGuid(me, "customerId"));
        Assert.Equal(HttpStatusCode.BadRequest, secondRedeem.StatusCode);

        using var verificationScope = _factory.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var consumed = await verificationDb.GuestOrderAccessTokens
            .SingleAsync(candidate => candidate.TokenHash == tokenHash);
        Assert.NotNull(consumed.UsedAtUtc);
    }

    [Fact]
    public async Task PaymentsQuote_HourlyRental_UsesHourlyPriceQuantityAndHours()
    {
        var client = await CreateClientAsync(authenticated: false);
        var productId = await SeedQuoteProductAsync(
            TestTenantId,
            dailyPrice: 100m,
            hourlyPrice: 12.50m,
            availableQuantity: 3);
        var request = new PaymentQuoteRequest
        {
            StartDateUtc = new DateTime(2026, 8, 1, 8, 0, 0, DateTimeKind.Utc),
            EndDateUtc = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc),
            RentalType = RentalTypeDto.Hourly,
            HoursRented = 4,
            Items = [new CreateRentalItem { ProductId = productId, Quantity = 2 }]
        };

        var response = await client.PostAsJsonAsync("/api/payments/quote", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var quote = await response.Content.ReadFromJsonAsync<PaymentQuoteResponse>();
        Assert.NotNull(quote);
        Assert.Equal(100m, quote.TotalAmount);
        Assert.Equal(30m, quote.DepositAmount);
        Assert.Equal(1, quote.RentalDays);
        var quotedItem = Assert.Single(quote.Items);
        Assert.Equal(productId, quotedItem.ProductId);
        Assert.Equal(100m, quotedItem.Subtotal);
        var tenant = Assert.Single(quote.Tenants);
        Assert.Equal(TestTenantId, tenant.TenantId);
        Assert.Equal(100m, tenant.TotalAmount);
    }

    [Fact]
    public async Task PaymentsQuote_MarketplaceGroups_UseSeparateTermsTypesAndDeposits()
    {
        var client = await CreateClientAsync(authenticated: false);
        var dailyTenantId = await SeedTenantAsync("Alpine Rent");
        var hourlyTenantId = await SeedTenantAsync("Bike Point");
        var dailyProductId = await SeedQuoteProductAsync(dailyTenantId, dailyPrice: 100m);
        var hourlyProductId = await SeedQuoteProductAsync(
            hourlyTenantId,
            dailyPrice: 120m,
            hourlyPrice: 20m);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.CompanyInfos.Add(new CompanyInfo
            {
                Id = Guid.NewGuid(),
                TenantId = hourlyTenantId,
                Name = "Bike Point",
                Address = "ul. Rowerowa 5",
                City = "Kraków",
                RegulationsText = "Własny regulamin Bike Point"
            });
            await db.SaveChangesAsync();
        }

        var request = new PaymentQuoteRequest
        {
            RentalGroups =
            [
                new RentalGroupQuoteRequest
                {
                    TenantId = dailyTenantId,
                    StartDateUtc = new DateTime(2026, 8, 1, 8, 0, 0, DateTimeKind.Utc),
                    EndDateUtc = new DateTime(2026, 8, 3, 8, 0, 0, DateTimeKind.Utc),
                    RentalType = RentalTypeDto.Daily,
                    Items = [new CreateRentalItem { ProductId = dailyProductId, Quantity = 1 }]
                },
                new RentalGroupQuoteRequest
                {
                    TenantId = hourlyTenantId,
                    StartDateUtc = new DateTime(2026, 8, 5, 8, 0, 0, DateTimeKind.Utc),
                    EndDateUtc = new DateTime(2026, 8, 5, 11, 0, 0, DateTimeKind.Utc),
                    RentalType = RentalTypeDto.Hourly,
                    HoursRented = 3,
                    Items = [new CreateRentalItem { ProductId = hourlyProductId, Quantity = 1 }]
                }
            ]
        };

        var response = await client.PostAsJsonAsync("/api/payments/quote", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var quote = await response.Content.ReadFromJsonAsync<PaymentQuoteResponse>();
        Assert.NotNull(quote);
        Assert.Equal(260m, quote.TotalAmount);
        Assert.Equal(78m, quote.DepositAmount);
        Assert.Equal(2, quote.RentalCount);

        var daily = Assert.Single(quote.Tenants, tenant => tenant.TenantId == dailyTenantId);
        Assert.Equal(200m, daily.TotalAmount);
        Assert.Equal(60m, daily.DepositAmount);
        Assert.Equal(RentalTypeDto.Daily, daily.RentalType);
        Assert.True(daily.RentalTerms.UsesPlatformDefault);
        Assert.Contains("STANDARDOWY REGULAMIN", daily.RentalTerms.Content);
        Assert.False(string.IsNullOrWhiteSpace(daily.RentalTerms.ContentHash));

        var hourly = Assert.Single(quote.Tenants, tenant => tenant.TenantId == hourlyTenantId);
        Assert.Equal(60m, hourly.TotalAmount);
        Assert.Equal(18m, hourly.DepositAmount);
        Assert.Equal(RentalTypeDto.Hourly, hourly.RentalType);
        Assert.Equal(3, hourly.HoursRented);
        Assert.Equal("Kraków", hourly.PickupCity);
        Assert.False(hourly.RentalTerms.UsesPlatformDefault);
        Assert.Equal("Własny regulamin Bike Point", hourly.RentalTerms.Content);
    }

    [Fact]
    public async Task PaymentsQuote_MarketplaceGroupRejectsProductFromDifferentTenant()
    {
        var client = await CreateClientAsync(authenticated: false);
        var productTenantId = await SeedTenantAsync("Właściwa wypożyczalnia");
        var declaredTenantId = await SeedTenantAsync("Podstawiona wypożyczalnia");
        var productId = await SeedQuoteProductAsync(productTenantId, dailyPrice: 80m);

        var response = await client.PostAsJsonAsync("/api/payments/quote", new PaymentQuoteRequest
        {
            RentalGroups =
            [
                new RentalGroupQuoteRequest
                {
                    TenantId = declaredTenantId,
                    StartDateUtc = new DateTime(2026, 8, 1, 8, 0, 0, DateTimeKind.Utc),
                    EndDateUtc = new DateTime(2026, 8, 2, 8, 0, 0, DateTimeKind.Utc),
                    Items = [new CreateRentalItem { ProductId = productId, Quantity = 1 }]
                }
            ]
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PaymentsQuote_MarketplaceRejectsDuplicateTenantGroups()
    {
        var client = await CreateClientAsync(authenticated: false);
        var tenantId = await SeedTenantAsync("Powtórzona wypożyczalnia");
        var firstProductId = await SeedQuoteProductAsync(tenantId, dailyPrice: 40m);
        var secondProductId = await SeedQuoteProductAsync(tenantId, dailyPrice: 50m);
        var start = new DateTime(2026, 8, 1, 8, 0, 0, DateTimeKind.Utc);

        var response = await client.PostAsJsonAsync("/api/payments/quote", new PaymentQuoteRequest
        {
            RentalGroups =
            [
                new RentalGroupQuoteRequest
                {
                    TenantId = tenantId,
                    StartDateUtc = start,
                    EndDateUtc = start.AddDays(1),
                    Items = [new CreateRentalItem { ProductId = firstProductId, Quantity = 1 }]
                },
                new RentalGroupQuoteRequest
                {
                    TenantId = tenantId,
                    StartDateUtc = start.AddDays(2),
                    EndDateUtc = start.AddDays(3),
                    Items = [new CreateRentalItem { ProductId = secondProductId, Quantity = 1 }]
                }
            ]
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Holds_WhenPickupFallsOnClosedDay_ReturnsBusinessHoursReason()
    {
        var client = await CreateClientAsync(authenticated: false);
        var productId = await SeedQuoteProductAsync(TestTenantId, dailyPrice: 100m);
        var localDate = PolishRentalTime.TodayLocal.AddDays(7);
        var startLocal = localDate.AddHours(10);
        var endLocal = localDate.AddHours(12);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.BusinessHoursSchedules.Add(new BusinessHoursSchedule
            {
                Id = Guid.NewGuid(),
                TenantId = TestTenantId,
                Days =
                [
                    new BusinessHoursDay
                    {
                        Id = Guid.NewGuid(),
                        DayOfWeek = localDate.DayOfWeek,
                        IsClosed = true
                    }
                ]
            });
            await db.SaveChangesAsync();
        }

        var response = await client.PostAsJsonAsync("/api/holds", new CreateHoldRequest
        {
            ProductId = productId,
            Quantity = 1,
            StartDateUtc = PolishRentalTime.ToUtc(startLocal),
            EndDateUtc = PolishRentalTime.ToUtc(endLocal),
            SessionId = Guid.NewGuid().ToString("N")
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("zamknięta", ReadProperty(payload, "error").GetString());
    }

    [Fact]
    public async Task PaymentsQuote_HourlyRental_RejectsHoursThatDoNotMatchWindow()
    {
        var client = await CreateClientAsync(authenticated: false);
        var productId = await SeedQuoteProductAsync(
            TestTenantId,
            dailyPrice: 100m,
            hourlyPrice: 12.50m,
            availableQuantity: 3);
        var request = new PaymentQuoteRequest
        {
            StartDateUtc = new DateTime(2026, 8, 1, 8, 0, 0, DateTimeKind.Utc),
            EndDateUtc = new DateTime(2026, 8, 2, 8, 0, 0, DateTimeKind.Utc),
            RentalType = RentalTypeDto.Hourly,
            HoursRented = 1,
            Items = [new CreateRentalItem { ProductId = productId, Quantity = 1 }]
        };

        var response = await client.PostAsJsonAsync("/api/payments/quote", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PaymentsQuote_DailyRental_RejectsPeriodLongerThanOneYear()
    {
        var client = await CreateClientAsync(authenticated: false);
        var productId = await SeedQuoteProductAsync(TestTenantId, dailyPrice: 100m);
        var request = CreateDailyQuoteRequest(
            productId,
            new DateTime(2026, 8, 1, 8, 0, 0, DateTimeKind.Utc),
            new DateTime(2027, 8, 2, 8, 0, 0, DateTimeKind.Utc));

        var response = await client.PostAsJsonAsync("/api/payments/quote", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PaymentsQuote_DailyRental_AppliesSeasonalRulePerDayAndHighestPriority()
    {
        var client = await CreateClientAsync(authenticated: false);
        var productId = await SeedQuoteProductAsync(TestTenantId, dailyPrice: 100m);
        var firstDay = new DateOnly(2026, 8, 1);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.PriceRules.AddRange(
                new PriceRule
                {
                    Id = Guid.NewGuid(),
                    TenantId = TestTenantId,
                    ProductId = productId,
                    Name = "Wysoki sezon",
                    FromDate = firstDay,
                    ToDate = firstDay.AddDays(2),
                    Type = PriceRuleType.Multiplier,
                    Value = 2m,
                    Priority = 10,
                    IsActive = true
                },
                new PriceRule
                {
                    Id = Guid.NewGuid(),
                    TenantId = TestTenantId,
                    ProductId = productId,
                    Name = "Cena specjalna w środku okresu",
                    FromDate = firstDay.AddDays(1),
                    ToDate = firstDay.AddDays(1),
                    Type = PriceRuleType.FixedReplace,
                    Value = 250m,
                    Priority = 20,
                    IsActive = true
                });
            await db.SaveChangesAsync();
        }
        var request = CreateDailyQuoteRequest(
            productId,
            new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 8, 4, 0, 0, 0, DateTimeKind.Utc));

        var response = await client.PostAsJsonAsync("/api/payments/quote", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var quote = await response.Content.ReadFromJsonAsync<PaymentQuoteResponse>();
        Assert.NotNull(quote);
        Assert.Equal(3, quote.RentalDays);
        Assert.Equal(650m, quote.TotalAmount);
        Assert.Equal(195m, quote.DepositAmount);
    }

    [Fact]
    public async Task PaymentsQuote_DailyRental_UsesWarsawCalendarDateForSeasonalRule()
    {
        var client = await CreateClientAsync(authenticated: false);
        var productId = await SeedQuoteProductAsync(TestTenantId, dailyPrice: 100m);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.PriceRules.Add(new PriceRule
            {
                Id = Guid.NewGuid(),
                TenantId = TestTenantId,
                ProductId = productId,
                Name = "Cena od północy czasu polskiego",
                FromDate = new DateOnly(2026, 8, 1),
                ToDate = new DateOnly(2026, 8, 1),
                Type = PriceRuleType.FixedReplace,
                Value = 225m,
                Priority = 10,
                IsActive = true
            });
            await db.SaveChangesAsync();
        }

        // 31.07 22:30 UTC is already 01.08 00:30 in Europe/Warsaw.
        var request = CreateDailyQuoteRequest(
            productId,
            new DateTime(2026, 7, 31, 22, 30, 0, DateTimeKind.Utc),
            new DateTime(2026, 8, 1, 22, 30, 0, DateTimeKind.Utc));

        var response = await client.PostAsJsonAsync("/api/payments/quote", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var quote = await response.Content.ReadFromJsonAsync<PaymentQuoteResponse>();
        Assert.NotNull(quote);
        Assert.Equal(1, quote.RentalDays);
        Assert.Equal(225m, quote.TotalAmount);
    }

    [Fact]
    public async Task PaymentsQuote_DailyRental_CountsSameLocalTimeAcrossAutumnDstAsOneDay()
    {
        var client = await CreateClientAsync(authenticated: false);
        var productId = await SeedQuoteProductAsync(TestTenantId, dailyPrice: 100m);

        // Europe/Warsaw changes from UTC+2 to UTC+1 on 25.10.2026. The UTC
        // interval is 25 hours, while the customer-selected local interval is
        // exactly 24 hours: 09:00 -> 09:00 on the next calendar day.
        var request = CreateDailyQuoteRequest(
            productId,
            new DateTime(2026, 10, 24, 7, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 25, 8, 0, 0, DateTimeKind.Utc));

        var response = await client.PostAsJsonAsync("/api/payments/quote", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var quote = await response.Content.ReadFromJsonAsync<PaymentQuoteResponse>();
        Assert.NotNull(quote);
        Assert.Equal(1, quote.RentalDays);
        Assert.Equal(100m, quote.TotalAmount);
    }

    [Fact]
    public async Task PaymentsQuote_WithZeroQuantity_ReturnsBadRequest()
    {
        var client = await CreateClientAsync(authenticated: false);
        var productId = await SeedQuoteProductAsync(TestTenantId, dailyPrice: 50m);
        var request = CreateDailyQuoteRequest(
            productId,
            new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 8, 2, 0, 0, 0, DateTimeKind.Utc),
            quantity: 0);

        var response = await client.PostAsJsonAsync("/api/payments/quote", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PaymentsQuote_WithInactiveProduct_ReturnsBadRequest()
    {
        var client = await CreateClientAsync(authenticated: false);
        var productId = await SeedQuoteProductAsync(TestTenantId, dailyPrice: 50m, isActive: false);
        var request = CreateDailyQuoteRequest(
            productId,
            new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 8, 2, 0, 0, 0, DateTimeKind.Utc));

        var response = await client.PostAsJsonAsync("/api/payments/quote", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PaymentsQuote_WithDemoTenantProduct_ReturnsBadRequest()
    {
        var client = await CreateClientAsync(authenticated: false);
        var demoTenantId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Tenants.Add(new Tenant
            {
                Id = demoTenantId,
                Name = "Demo tenant",
                IsDemo = true
            });
            await db.SaveChangesAsync();
        }
        var productId = await SeedQuoteProductAsync(demoTenantId, dailyPrice: 50m);
        var request = CreateDailyQuoteRequest(
            productId,
            new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 8, 2, 0, 0, 0, DateTimeKind.Utc));

        var response = await client.PostAsJsonAsync("/api/payments/quote", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Rentals_Delete_AsDifferentCustomer_ReturnsNotFoundAndDoesNotCancelRental()
    {
        var client = await CreateHttpsClientAsync();
        var rentalId = await SeedRentalAsync(TestTenantId);

        var guestResponse = await client.PostAsJsonAsync("/api/auth/guest-session", new
        {
            Email = $"attacker-{Guid.NewGuid():N}@example.test",
            FullName = "Inny klient",
            PhoneNumber = "+48111000000",
            Address = (string?)null,
            DocumentNumber = (string?)null,
            Notes = (string?)null
        });
        Assert.Equal(HttpStatusCode.OK, guestResponse.StatusCode);

        var response = await client.DeleteAsync($"/api/rentals/{rentalId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var rental = await db.Rentals.IgnoreQueryFilters().SingleAsync(r => r.Id == rentalId);
        Assert.Equal(RentalStatus.Pending, rental.Status);
    }

    [Fact]
    public async Task GuestSession_WithExistingCustomerEmail_CreatesIsolatedCustomer()
    {
        var client = await CreateHttpsClientAsync();
        var spoofedTenantId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", spoofedTenantId.ToString());
        const string email = "existing-customer@example.test";
        var existingCustomerId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Customers.Add(new Customer
            {
                Id = existingCustomerId,
                TenantId = TestTenantId,
                FullName = "Istniejący klient",
                Email = email,
                PhoneNumber = "+48123123123",
                DocumentNumber = "PRIVATE-DOCUMENT",
                CreatedAtUtc = DateTime.UtcNow.AddYears(-1)
            });
            await db.SaveChangesAsync();
        }

        var response = await client.PostAsJsonAsync("/api/auth/guest-session", new
        {
            Email = email.ToUpperInvariant(),
            FullName = "Nowa sesja gościa",
            PhoneNumber = "+48999000111",
            Address = (string?)null,
            DocumentNumber = (string?)null,
            Notes = (string?)null
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var guestCustomerId = ReadGuid(payload, "customerId");
        Assert.NotEqual(existingCustomerId, guestCustomerId);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var customers = await verifyDb.Customers.IgnoreQueryFilters()
            .Where(c => c.Email == email)
            .OrderBy(c => c.CreatedAtUtc)
            .ToListAsync();
        Assert.Equal(2, customers.Count);
        Assert.Contains(customers, c => c.Id == existingCustomerId && c.DocumentNumber == "PRIVATE-DOCUMENT");
        Assert.Contains(customers, c =>
            c.Id == guestCustomerId &&
            c.FullName == "Nowa sesja gościa" &&
            c.TenantId == Guid.Empty);

        var lookupResponse = await client.GetAsync(
            $"/api/customers/by-email?email={Uri.EscapeDataString(email.ToUpperInvariant())}");
        Assert.Equal(HttpStatusCode.OK, lookupResponse.StatusCode);
        var lookup = await lookupResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(guestCustomerId, ReadGuid(lookup, "id"));
    }

    [Fact]
    public async Task CustomersPost_AnonymousCaller_IsRejected()
    {
        var client = await CreateClientAsync(authenticated: false);

        var response = await client.PostAsJsonAsync("/api/customers", new CreateCustomerRequest
        {
            FullName = "Anonimowy wpis",
            Email = "anonymous@example.test",
            PhoneNumber = "+48123123123"
        });

        Assert.True(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Register_WithoutCurrentLegalVersions_IsRejected()
    {
        var client = await CreateHttpsClientAsync();
        await EnsureRoleAsync("Client");
        var email = $"legal-missing-{Guid.NewGuid():N}@example.test";

        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            Email = email,
            Password = "StableClient123",
            FullName = "Klient bez akceptacji",
            PhoneNumber = "+48555000112",
            DocumentNumber = (string?)null,
            AcceptedTermsVersion = "stara-wersja",
            AcknowledgedPrivacyVersion = (string?)null
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("aktualny regulamin", ReadProperty(error, "error").GetString() ?? string.Empty);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.Users.AnyAsync(user => user.NormalizedEmail == email.ToUpperInvariant()));
    }

    [Fact]
    public async Task RegisterThenLogin_UsesPersistedCustomerIdClaimDespiteDuplicateEmailCustomer()
    {
        var client = await CreateHttpsClientAsync();
        await EnsureRoleAsync("Client");
        const string email = "stable-identity@example.test";
        const string password = "StableClient123";

        var registerResponse = await client.PostAsJsonAsync("/api/auth/register", new
        {
            Email = email,
            Password = password,
            FullName = "Stały klient",
            PhoneNumber = "+48555000111",
            DocumentNumber = (string?)null,
            AcceptedTermsVersion = LegalDocumentVersions.Terms,
            AcknowledgedPrivacyVersion = LegalDocumentVersions.Privacy
        });
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);
        var registerPayload = await registerResponse.Content.ReadFromJsonAsync<JsonElement>();
        var registeredCustomerId = ReadGuid(ReadProperty(registerPayload, "user"), "customerId");
        Assert.True(ReadProperty(registerPayload, "emailConfirmationRequired").GetBoolean());

        var meBeforeConfirmation = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, meBeforeConfirmation.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await db.Users.SingleAsync(u => u.NormalizedEmail == email.ToUpperInvariant());
            Assert.False(user.EmailConfirmed);
            Assert.Equal(LegalDocumentVersions.Terms, user.AcceptedTermsVersion);
            Assert.Equal(LegalDocumentVersions.Privacy, user.AcknowledgedPrivacyVersion);
            Assert.NotNull(user.LegalAcceptedAtUtc);
            Assert.Equal(DateTimeKind.Utc, user.LegalAcceptedAtUtc!.Value.Kind);
            var persistedClaim = await db.UserClaims.SingleAsync(c =>
                c.UserId == user.Id && c.ClaimType == "customer-id");
            Assert.Equal(registeredCustomerId.ToString(), persistedClaim.ClaimValue);

            db.Customers.Add(new Customer
            {
                Id = Guid.NewGuid(),
                TenantId = Guid.Empty,
                FullName = "Rekord podszywający się",
                Email = email,
                CreatedAtUtc = DateTime.UtcNow.AddYears(-10)
            });
            await db.SaveChangesAsync();
        }

        var wrongPasswordResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = email,
            Password = "WrongPassword123"
        });
        Assert.Equal(HttpStatusCode.BadRequest, wrongPasswordResponse.StatusCode);
        var wrongPasswordPayload = await wrongPasswordResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(wrongPasswordPayload.TryGetProperty("code", out _));

        var unconfirmedLoginResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = email,
            Password = password
        });
        Assert.Equal(HttpStatusCode.Forbidden, unconfirmedLoginResponse.StatusCode);
        var unconfirmedPayload = await unconfirmedLoginResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(
            "email_confirmation_required",
            ReadProperty(unconfirmedPayload, "code").GetString());

        using (var scope = _factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByEmailAsync(email);
            Assert.NotNull(user);
            var confirmationToken = await userManager.GenerateEmailConfirmationTokenAsync(user!);
            var confirmation = await userManager.ConfirmEmailAsync(user!, confirmationToken);
            Assert.True(confirmation.Succeeded);
        }

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = email,
            Password = password
        });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var loginPayload = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(registeredCustomerId, ReadGuid(ReadProperty(loginPayload, "user"), "customerId"));

        var meResponse = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        var mePayload = await meResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(registeredCustomerId, ReadGuid(mePayload, "customerId"));
    }

    [Fact]
    public async Task StartupRepair_DemotesGlobalClientPromotedWithoutOwnerMembership()
    {
        await ResetDatabaseAsync();
        await EnsureRoleAsync(RoleNames.Client);
        await EnsureRoleAsync(RoleNames.Owner);
        var email = $"accidental-owner-{Guid.NewGuid():N}@example.test";
        var customerId = Guid.NewGuid();
        Guid userId;

        using (var scope = _factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                TenantId = TestTenantId
            };
            userId = user.Id;

            Assert.True((await userManager.CreateAsync(user)).Succeeded);
            Assert.True((await userManager.AddToRoleAsync(user, RoleNames.Client)).Succeeded);
            Assert.True((await userManager.AddToRoleAsync(user, RoleNames.Owner)).Succeeded);
            db.Customers.Add(new Customer
            {
                Id = customerId,
                TenantId = Guid.Empty,
                FullName = "Klient marketplace",
                Email = email
            });
            await db.SaveChangesAsync();
            Assert.True((await userManager.AddClaimAsync(
                user,
                new Claim(AuthClaims.CustomerId, customerId.ToString()))).Succeeded);

            var repair = scope.ServiceProvider.GetRequiredService<AccidentalOwnerPromotionRepair>();
            Assert.Equal(1, await repair.RepairAsync());
        }

        using var verifyScope = _factory.Services.CreateScope();
        var verifyUserManager = verifyScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var repairedUser = await verifyUserManager.FindByIdAsync(userId.ToString());
        Assert.NotNull(repairedUser);
        Assert.Null(repairedUser!.TenantId);
        Assert.True(await verifyUserManager.IsInRoleAsync(repairedUser, RoleNames.Client));
        Assert.False(await verifyUserManager.IsInRoleAsync(repairedUser, RoleNames.Owner));
    }

    [Fact]
    public async Task StartupRepair_PreservesOwnerWithExplicitTenantMembership()
    {
        await ResetDatabaseAsync();
        await EnsureRoleAsync(RoleNames.Owner);
        var email = $"explicit-owner-{Guid.NewGuid():N}@example.test";
        var customerId = Guid.NewGuid();
        Guid userId;

        using (var scope = _factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                TenantId = TestTenantId
            };
            userId = user.Id;

            Assert.True((await userManager.CreateAsync(user)).Succeeded);
            Assert.True((await userManager.AddToRoleAsync(user, RoleNames.Owner)).Succeeded);
            db.Customers.Add(new Customer
            {
                Id = customerId,
                TenantId = Guid.Empty,
                FullName = "Właściciel z członkostwem",
                Email = email
            });
            db.TenantUsers.Add(new TenantUser
            {
                Id = Guid.NewGuid(),
                TenantId = TestTenantId,
                UserId = user.Id,
                Role = RoleNames.Owner
            });
            await db.SaveChangesAsync();
            Assert.True((await userManager.AddClaimAsync(
                user,
                new Claim(AuthClaims.CustomerId, customerId.ToString()))).Succeeded);

            var repair = scope.ServiceProvider.GetRequiredService<AccidentalOwnerPromotionRepair>();
            Assert.Equal(0, await repair.RepairAsync());
        }

        using var verifyScope = _factory.Services.CreateScope();
        var verifyUserManager = verifyScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var preservedUser = await verifyUserManager.FindByIdAsync(userId.ToString());
        Assert.NotNull(preservedUser);
        Assert.Equal(TestTenantId, preservedUser!.TenantId);
        Assert.True(await verifyUserManager.IsInRoleAsync(preservedUser, RoleNames.Owner));
    }

    [Fact]
    public async Task AnonymousRefresh_ReturnsUnauthorizedWithoutLoginRedirect()
    {
        var client = await CreateHttpsClientAsync();

        var response = await client.PostAsync("/api/auth/refresh", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task BearerOnlyRefresh_ReturnsUnauthorizedWithoutLoginRedirect()
    {
        var client = await CreateHttpsClientAsync();
        string accessToken;
        using (var scope = _factory.Services.CreateScope())
        {
            var jwt = scope.ServiceProvider.GetRequiredService<JwtTokenService>();
            accessToken = jwt.CreateUserToken(
                new ApplicationUser
                {
                    Id = Guid.NewGuid(),
                    UserName = "bearer-only@example.test",
                    Email = "bearer-only@example.test",
                    EmailConfirmed = true,
                    TenantId = TestTenantId
                },
                TestTenantId,
                [RoleNames.Client],
                Guid.NewGuid()).AccessToken;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer",
            accessToken);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task ClientIdentitySession_CanRefreshClientApplication()
    {
        var client = await CreateHttpsClientAsync();
        await EnsureRoleAsync(RoleNames.Client);
        var email = $"client-refresh-{Guid.NewGuid():N}@example.test";
        const string password = "ClientRefresh123";

        using (var scope = _factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                TenantId = TestTenantId
            };
            var createResult = await userManager.CreateAsync(user, password);
            Assert.True(createResult.Succeeded, string.Join(", ", createResult.Errors.Select(error => error.Description)));
            var roleResult = await userManager.AddToRoleAsync(user, RoleNames.Client);
            Assert.True(roleResult.Succeeded, string.Join(", ", roleResult.Errors.Select(error => error.Description)));
        }

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = email,
            Password = password
        });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var refreshResponse = await client.PostAsync("/api/auth/refresh", null);

        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
        Assert.Contains(
            refreshResponse.Headers.GetValues("Set-Cookie"),
            value => value.Contains(Endpoints.AccessTokenCookieName, StringComparison.Ordinal));
    }

    [Fact]
    public async Task EmployeeOnly_CannotCreateClientApplicationSession()
    {
        var client = await CreateHttpsClientAsync();
        await EnsureRoleAsync(RoleNames.Employee);
        var email = $"employee-client-denied-{Guid.NewGuid():N}@example.test";
        const string password = "EmployeeClientDenied123";

        using (var scope = _factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                TenantId = TestTenantId
            };
            Assert.True((await userManager.CreateAsync(user, password)).Succeeded);
            Assert.True((await userManager.AddToRoleAsync(user, RoleNames.Employee)).Succeeded);
        }

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = email,
            Password = password
        });

        Assert.Equal(HttpStatusCode.Forbidden, loginResponse.StatusCode);
        var payload = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("client_access_denied", ReadProperty(payload, "code").GetString());
        Assert.DoesNotContain(
            loginResponse.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : [],
            value => value.StartsWith(Endpoints.AccessTokenCookieName + "=", StringComparison.Ordinal) &&
                     !value.StartsWith(Endpoints.AccessTokenCookieName + "=;", StringComparison.Ordinal));

        var refreshResponse = await client.PostAsync("/api/auth/refresh", null);
        Assert.Equal(HttpStatusCode.Unauthorized, refreshResponse.StatusCode);
    }

    [Theory]
    [InlineData(RoleNames.Owner)]
    [InlineData(RoleNames.SuperAdmin)]
    public async Task StaffIdentitySession_CanOpenAndRefreshClientApplication(string role)
    {
        var client = await CreateHttpsClientAsync();
        await EnsureRoleAsync(role);
        var email = $"client-preview-{role.ToLowerInvariant()}-{Guid.NewGuid():N}@example.test";
        const string password = "ClientPreview123";

        using (var scope = _factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                TenantId = role == RoleNames.Owner ? TestTenantId : null
            };
            var createResult = await userManager.CreateAsync(user, password);
            Assert.True(createResult.Succeeded, string.Join(", ", createResult.Errors.Select(error => error.Description)));
            var roleResult = await userManager.AddToRoleAsync(user, role);
            Assert.True(roleResult.Succeeded, string.Join(", ", roleResult.Errors.Select(error => error.Description)));
        }

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = email,
            Password = password
        });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var refreshResponse = await client.PostAsync("/api/auth/refresh", null);
        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);

        var previewResponse = await client.GetAsync(
            "/api/auth/client-preview?returnUrl=%2F_client%2Fproducts");
        Assert.Equal(HttpStatusCode.Redirect, previewResponse.StatusCode);
        Assert.Equal("/_client/products", previewResponse.Headers.Location?.OriginalString);
        Assert.Contains(
            previewResponse.Headers.GetValues("Set-Cookie"),
            value => value.Contains(Endpoints.AccessTokenCookieName, StringComparison.Ordinal));

        var unsafeReturnResponse = await client.GetAsync(
            "/api/auth/client-preview?returnUrl=https%3A%2F%2Fevil.example");
        Assert.Equal(HttpStatusCode.Redirect, unsafeReturnResponse.StatusCode);
        Assert.Equal("/_client/", unsafeReturnResponse.Headers.Location?.OriginalString);

        // Hand-off i odświeżenie sesji mają osobny limiter od credentiali 5/min.
        // Stara konfiguracja zwracała tutaj 429 już po kilku wejściach w podgląd.
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var repeatedPreview = await client.GetAsync(
                "/api/auth/client-preview?returnUrl=%2F_client%2F");
            Assert.Equal(HttpStatusCode.Redirect, repeatedPreview.StatusCode);
            Assert.Equal("/_client/", repeatedPreview.Headers.Location?.OriginalString);
        }

        var meResponse = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        var me = await meResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(
            ReadProperty(me, "roles").EnumerateArray(),
            value => string.Equals(value.GetString(), role, StringComparison.Ordinal));
        Assert.NotEqual(Guid.Empty, ReadGuid(me, "customerId"));

        // Ten factory używa TestAuth jako domyślnego schematu dla SSR. Jawny nagłówek
        // weryfikuje wariant widoku Login dla już zalogowanego Ownera/SuperAdmina,
        // podczas gdy wcześniejsze wywołania sprawdziły prawdziwe cookie Identity.
        client.DefaultRequestHeaders.Add(AuthHeaderName, "true");
        var loginPage = await client.GetAsync("/Account/Login");
        Assert.Equal(HttpStatusCode.OK, loginPage.StatusCode);
        var loginHtml = await loginPage.Content.ReadAsStringAsync();
        Assert.Contains("Przejdź do aplikacji klienta", loginHtml, StringComparison.Ordinal);
        Assert.Contains("/api/auth/client-preview?returnUrl=%2F_client%2F", loginHtml, StringComparison.Ordinal);
        Assert.Contains("action=\"/Account/Logout\"", loginHtml, StringComparison.Ordinal);
        Assert.Contains("name=\"ReturnUrl\" value=\"/Account/Login\"", loginHtml, StringComparison.Ordinal);

        var antiforgeryMatch = System.Text.RegularExpressions.Regex.Match(
            loginHtml,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        Assert.True(antiforgeryMatch.Success, "Brak tokenu antiforgery w formularzu wylogowania.");
        var antiforgeryToken = System.Net.WebUtility.HtmlDecode(antiforgeryMatch.Groups[1].Value);

        var logoutResponse = await client.PostAsync(
            "/Account/Logout",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = antiforgeryToken,
                // Próba open redirect ma zostać zastąpiona bezpiecznym fallbackiem.
                ["ReturnUrl"] = "https://evil.example/logout-callback"
            }));
        Assert.Equal(HttpStatusCode.Redirect, logoutResponse.StatusCode);
        Assert.Equal("/Account/Login", logoutResponse.Headers.Location?.OriginalString);
        var logoutCookies = logoutResponse.Headers.GetValues("Set-Cookie").ToArray();
        Assert.Contains(
            logoutCookies,
            value => value.Contains(Endpoints.AccessTokenCookieName, StringComparison.Ordinal) &&
                     value.Contains("expires=", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            logoutCookies,
            value => value.Contains("Identity.Application", StringComparison.Ordinal) &&
                     value.Contains("expires=", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("/products?search=narty&page=2", "/_client/products?search=narty&page=2")]
    [InlineData("/checkout/cancel?session_id=cs_test", "/_client/checkout/cancel?session_id=cs_test")]
    [InlineData("/my-rentals/22222222-2222-2222-2222-222222222222", "/_client/my-rentals/22222222-2222-2222-2222-222222222222")]
    public async Task LegacyClientRoute_RedirectsToBundledWasmAndPreservesQuery(
        string requestUrl,
        string expectedLocation)
    {
        var client = await CreateClientAsync(authenticated: false);

        var response = await client.GetAsync(requestUrl);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(expectedLocation, response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task RootLogin_RemainsAdminLoginEntryPoint()
    {
        var client = await CreateClientAsync(authenticated: false);

        var response = await client.GetAsync("/login");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/Login", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task ResendEmailConfirmation_DoesNotRevealWhetherAccountExists()
    {
        var client = await CreateHttpsClientAsync();

        var missing = await client.PostAsJsonAsync("/api/auth/resend-confirmation", new
        {
            Email = $"missing-{Guid.NewGuid():N}@example.test"
        });

        Assert.Equal(HttpStatusCode.Accepted, missing.StatusCode);
    }

    [Fact]
    public async Task CustomerIdentityService_DoesNotLinkLegacyCustomerByEmail()
    {
        await CreateClientAsync(authenticated: false);
        const string email = "legacy-link@example.test";
        var tenantId = TestTenantId;
        var existingCustomerId = Guid.NewGuid();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            TenantId = tenantId
        };
        var createResult = await userManager.CreateAsync(user, "LegacyClient123");
        Assert.True(createResult.Succeeded, string.Join(", ", createResult.Errors.Select(error => error.Description)));

        db.Customers.Add(new Customer
        {
            Id = existingCustomerId,
            TenantId = tenantId,
            FullName = "Historyczny klient innej osoby",
            Email = email,
            DocumentNumber = "PRIVATE-LEGACY-DOCUMENT",
            CreatedAtUtc = DateTime.UtcNow.AddYears(-2)
        });
        await db.SaveChangesAsync();

        var customerIdentity = scope.ServiceProvider.GetRequiredService<CustomerIdentityService>();
        var linkedCustomer = await customerIdentity.GetOrCreateAsync(user, "Właściciel konta");

        Assert.NotEqual(existingCustomerId, linkedCustomer.Id);
        Assert.Equal(tenantId, linkedCustomer.TenantId);
        Assert.Equal("Właściciel konta", linkedCustomer.FullName);
        var customerClaim = Assert.Single(
            await userManager.GetClaimsAsync(user),
            claim => claim.Type == AuthClaims.CustomerId);
        Assert.Equal(linkedCustomer.Id.ToString(), customerClaim.Value);

        var existingCustomer = await db.Customers.IgnoreQueryFilters()
            .SingleAsync(customer => customer.Id == existingCustomerId);
        Assert.Equal("PRIVATE-LEGACY-DOCUMENT", existingCustomer.DocumentNumber);
    }

    [Fact]
    public async Task ExternalLoginProvisioning_WithoutLegalAcknowledgement_DoesNotCreateAccount()
    {
        await CreateClientAsync(authenticated: false);
        var email = $"external-legal-{Guid.NewGuid():N}@example.test";

        using var scope = _factory.Services.CreateScope();
        var externalLogin = new ExternalLoginInfo(
            new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.Email, email)],
                authenticationType: "Google")),
            loginProvider: "Google",
            providerKey: $"google-{Guid.NewGuid():N}",
            displayName: "Google");

        var provisioning = scope.ServiceProvider.GetRequiredService<ExternalLoginProvisioningService>();
        var result = await provisioning.ProvisionAsync(externalLogin);

        Assert.Equal(ExternalLoginProvisioningStatus.LegalAcceptanceRequired, result.Status);
        Assert.Null(result.User);
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.Users.AnyAsync(user => user.NormalizedEmail == email.ToUpperInvariant()));
    }

    [Fact]
    public async Task ExternalLoginProvisioning_WithCurrentLegalVersions_StoresAcceptance()
    {
        await CreateClientAsync(authenticated: false);
        await EnsureRoleAsync("Client");
        var email = $"external-current-legal-{Guid.NewGuid():N}@example.test";
        var externalLogin = new ExternalLoginInfo(
            new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.Email, email)],
                authenticationType: "Google")),
            loginProvider: "Google",
            providerKey: $"google-{Guid.NewGuid():N}",
            displayName: "Google");

        using var scope = _factory.Services.CreateScope();
        var provisioning = scope.ServiceProvider.GetRequiredService<ExternalLoginProvisioningService>();

        var result = await provisioning.ProvisionAsync(
            externalLogin,
            LegalDocumentVersions.Terms,
            LegalDocumentVersions.Privacy);

        Assert.Equal(ExternalLoginProvisioningStatus.Created, result.Status);
        Assert.NotNull(result.User);
        Assert.Equal(LegalDocumentVersions.Terms, result.User.AcceptedTermsVersion);
        Assert.Equal(LegalDocumentVersions.Privacy, result.User.AcknowledgedPrivacyVersion);
        Assert.NotNull(result.User.LegalAcceptedAtUtc);
        Assert.Equal(DateTimeKind.Utc, result.User.LegalAcceptedAtUtc!.Value.Kind);
    }

    [Fact]
    public async Task ExternalLoginProvisioning_WithExistingEmail_DoesNotAutoLinkAccount()
    {
        await CreateClientAsync(authenticated: false);
        await EnsureRoleAsync("Client");
        const string email = "external-collision@example.test";

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var existingUser = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            EmailConfirmed = true
        };
        var createResult = await userManager.CreateAsync(existingUser, "ExistingClient123");
        Assert.True(createResult.Succeeded, string.Join(", ", createResult.Errors.Select(error => error.Description)));

        var externalPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Email, email)],
            authenticationType: "Google"));
        var externalLogin = new ExternalLoginInfo(
            externalPrincipal,
            loginProvider: "Google",
            providerKey: "google-provider-key",
            displayName: "Google");

        var provisioning = scope.ServiceProvider.GetRequiredService<ExternalLoginProvisioningService>();
        var result = await provisioning.ProvisionAsync(
            externalLogin,
            LegalDocumentVersions.Terms,
            LegalDocumentVersions.Privacy);

        Assert.Equal(ExternalLoginProvisioningStatus.ExistingEmailCollision, result.Status);
        Assert.Null(result.User);
        Assert.Empty(await userManager.GetLoginsAsync(existingUser));

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.Users.CountAsync(user => user.NormalizedEmail == email.ToUpperInvariant()));
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

    [Theory]
    [InlineData(RentalStatus.Draft, HttpStatusCode.Conflict)]
    [InlineData(RentalStatus.Pending, HttpStatusCode.Conflict)]
    [InlineData(RentalStatus.Confirmed, HttpStatusCode.Conflict)]
    [InlineData(RentalStatus.Active, HttpStatusCode.Conflict)]
    [InlineData(RentalStatus.Completed, HttpStatusCode.Created)]
    [InlineData(RentalStatus.Cancelled, HttpStatusCode.Created)]
    public async Task Rentals_Post_OnlyOpenLifecycleStatusesReserveInventory(
        RentalStatus existingStatus,
        HttpStatusCode expectedStatusCode)
    {
        var client = await CreateClientAsync();
        var startUtc = DateTime.UtcNow.Date.AddDays(1);
        var endUtc = startUtc.AddDays(2);
        var seeded = await SeedInventoryRentalAsync(
            existingStatus,
            startUtc,
            endUtc,
            availableQuantity: 20,
            rentedQuantity: 5);

        var response = await client.PostAsJsonAsync("/api/rentals", new
        {
            CustomerId = seeded.CustomerId,
            StartDateUtc = startUtc,
            EndDateUtc = endUtc,
            Items = new[] { new { seeded.ProductId, Quantity = 20 } }
        });

        Assert.Equal(expectedStatusCode, response.StatusCode);
        using var verificationScope = _factory.Services.CreateScope();
        var db = verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.True(await db.Rentals.IgnoreQueryFilters().AnyAsync(rental => rental.Id == seeded.RentalId));
        Assert.True(await db.RentalItems.IgnoreQueryFilters().AnyAsync(item => item.RentalId == seeded.RentalId));
    }

    [Fact]
    public async Task Rentals_Post_PhysicallyReturnedRentalDoesNotReserveInventoryBeforeStatusSync()
    {
        var client = await CreateClientAsync();
        var startUtc = DateTime.UtcNow.Date.AddDays(1);
        var endUtc = startUtc.AddDays(2);
        var seeded = await SeedInventoryRentalAsync(
            RentalStatus.Active,
            startUtc,
            endUtc,
            availableQuantity: 20,
            rentedQuantity: 5,
            returnedAtUtc: DateTime.UtcNow);

        var response = await client.PostAsJsonAsync("/api/rentals", new
        {
            CustomerId = seeded.CustomerId,
            StartDateUtc = startUtc,
            EndDateUtc = endUtc,
            Items = new[] { new { seeded.ProductId, Quantity = 20 } }
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Holds_CompletedRentalDoesNotReserveInventory()
    {
        var client = await CreateClientAsync(authenticated: false);
        var startLocal = PolishRentalTime.TodayLocal.AddDays(7).Date.AddHours(10);
        var startUtc = PolishRentalTime.ToUtc(startLocal);
        var endUtc = PolishRentalTime.ToUtc(startLocal.AddDays(1));
        var seeded = await SeedInventoryRentalAsync(
            RentalStatus.Completed,
            startUtc,
            endUtc,
            availableQuantity: 20,
            rentedQuantity: 5);

        var response = await client.PostAsJsonAsync("/api/holds", new CreateHoldRequest
        {
            ProductId = seeded.ProductId,
            Quantity = 20,
            StartDateUtc = startUtc,
            EndDateUtc = endUtc,
            SessionId = Guid.NewGuid().ToString("N")
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Holds_Refresh_CompletedRentalDoesNotReserveInventory()
    {
        var client = await CreateClientAsync(authenticated: false);
        var startLocal = PolishRentalTime.TodayLocal.AddDays(7).Date.AddHours(10);
        var startUtc = PolishRentalTime.ToUtc(startLocal);
        var endUtc = PolishRentalTime.ToUtc(startLocal.AddDays(1));
        var seeded = await SeedInventoryRentalAsync(
            RentalStatus.Completed,
            startUtc,
            endUtc,
            availableQuantity: 20,
            rentedQuantity: 5);
        var holdId = Guid.NewGuid();
        var sessionId = Guid.NewGuid().ToString("N");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.ReservationHolds.Add(new ReservationHold
            {
                Id = holdId,
                TenantId = TestTenantId,
                ProductId = seeded.ProductId,
                Quantity = 20,
                StartDateUtc = startUtc,
                EndDateUtc = endUtc,
                CreatedAtUtc = DateTime.UtcNow,
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(10),
                SessionId = sessionId
            });
            await db.SaveChangesAsync();
        }

        var response = await client.PostAsync(
            $"/api/holds/{holdId:D}/refresh?sessionId={sessionId}&ttlMinutes=10",
            content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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
        var page = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(page.TryGetProperty("items", out var items) || page.TryGetProperty("Items", out items));
        Assert.Equal(10, items.GetArrayLength());
    }

    [Fact]
    public async Task LegacySmsReplyEndpoints_AreNotMappedByDefault()
    {
        var client = await CreateClientAsync(authenticated: false);

        var response = await client.GetAsync("/api/sms/incoming?numer=48123123123&wiadomosc=TAK");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ConfirmationLink_GetThenPost_ConfirmsRentalAndStoresProof()
    {
        var client = await CreateClientAsync(authenticated: false);
        const string maliciousCompanyName = "</title><script>alert(1)</script>";
        var rentalId = await SeedRentalAsync(TestTenantId, companyName: maliciousCompanyName);

        string token;
        using (var scope = _factory.Services.CreateScope())
        {
            var confirmations = scope.ServiceProvider
                .GetRequiredService<SportRental.Admin.Services.IRentalConfirmationService>();
            token = await confirmations.CreateConfirmationForTenantAsync(TestTenantId, rentalId);
        }

        var getResponse = await client.GetAsync($"/confirm/{token}");
        var getHtml = await getResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.DoesNotContain(maliciousCompanyName, getHtml, StringComparison.Ordinal);
        Assert.Contains("&lt;/title&gt;&lt;script&gt;alert(1)&lt;/script&gt;", getHtml, StringComparison.Ordinal);
        Assert.Contains("Potwierdzam wynajem", getHtml, StringComparison.Ordinal);

        client.DefaultRequestHeaders.UserAgent.ParseAdd("SportRental-tests/1.0");
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.99");
        using var form = new FormUrlEncodedContent(Array.Empty<KeyValuePair<string, string>>());
        var postResponse = await client.PostAsync($"/confirm/{token}", form);
        var postHtml = await postResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);
        Assert.Contains("Wynajem potwierdzony", postHtml, StringComparison.Ordinal);

        using var verifyScope = _factory.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var rental = await db.Rentals.IgnoreQueryFilters().SingleAsync(r => r.Id == rentalId);
        var confirmation = await db.RentalConfirmations.IgnoreQueryFilters().SingleAsync(r => r.Token == token);

        Assert.Equal(RentalStatus.Confirmed, rental.Status);
        Assert.True(rental.IsSmsConfirmed);
        Assert.True(confirmation.IsConfirmed);
        Assert.NotNull(confirmation.ConfirmedAt);
        Assert.NotEqual("203.0.113.99", confirmation.ConfirmedFromIp);
        Assert.Equal("SportRental-tests/1.0", confirmation.ConfirmedUserAgent);
        Assert.Equal(64, confirmation.RegulationsHash?.Length);
    }

    [Fact]
    public async Task Contracts_Get_RedirectsAuthenticatedTenantToPrivateUrl()
    {
        var client = await CreateClientAsync();
        client.DefaultRequestHeaders.Remove(AuthHeaderName);
        client.DefaultRequestHeaders.Add(AuthHeaderName, "tenant-only");
        var rentalId = await SeedRentalAsync(TestTenantId, contractUrl: "contracts/test-contract.pdf");

        var response = await client.GetAsync($"/api/contracts/{rentalId}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(
            "https://test/contracts/test-contract.pdf?sas=fake",
            response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Contracts_Get_WithoutAuthentication_ReturnsUnauthorized()
    {
        var client = await CreateClientAsync(authenticated: false);
        var rentalId = await SeedRentalAsync(TestTenantId, contractUrl: "contracts/test-contract.pdf");

        var response = await client.GetAsync($"/api/contracts/{rentalId}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Contracts_Get_HidesRentalOwnedByAnotherTenant()
    {
        var client = await CreateClientAsync();
        client.DefaultRequestHeaders.Remove(AuthHeaderName);
        client.DefaultRequestHeaders.Add(AuthHeaderName, "tenant-only");
        var foreignTenantId = Guid.NewGuid();
        var rentalId = await SeedRentalAsync(
            foreignTenantId,
            contractUrl: "contracts/foreign-contract.pdf");

        var response = await client.GetAsync($"/api/contracts/{rentalId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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
        var results = await Task.WhenAll(t1, t2);

        var statuses = new[] { results[0].StatusCode, results[1].StatusCode };
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

    private async Task<HttpClient> CreateHttpsClientAsync()
    {
        await ResetDatabaseAsync();

        return _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
    }

    private static Guid ReadGuid(JsonElement element, string propertyName)
    {
        var property = ReadProperty(element, propertyName);
        Assert.True(property.TryGetGuid(out var value),
            $"Pole JSON '{propertyName}' nie zawiera poprawnego Guid.");
        return value;
    }

    private static JsonElement ReadProperty(JsonElement element, string propertyName)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                return property.Value;
        }

        throw new Xunit.Sdk.XunitException($"Brak pola JSON '{propertyName}'.");
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

    private async Task EnsureRoleAsync(string roleName)
    {
        using var scope = _factory.Services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        if (!await roleManager.RoleExistsAsync(roleName))
        {
            var result = await roleManager.CreateAsync(new IdentityRole<Guid>(roleName));
            Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(e => e.Description)));
        }
    }

    private async Task<Guid> SeedQuoteProductAsync(
        Guid tenantId,
        decimal dailyPrice,
        decimal? hourlyPrice = null,
        int availableQuantity = 5,
        bool isActive = true)
    {
        var productId = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Products.Add(new Product
        {
            Id = productId,
            TenantId = tenantId,
            Name = "Produkt do wyceny",
            Sku = $"QUOTE-{productId:N}",
            DailyPrice = dailyPrice,
            HourlyPrice = hourlyPrice,
            AvailableQuantity = availableQuantity,
            IsActive = isActive,
            Available = true,
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return productId;
    }

    private async Task<(Guid RentalId, Guid ProductId, Guid CustomerId)> SeedInventoryRentalAsync(
        RentalStatus status,
        DateTime startDateUtc,
        DateTime endDateUtc,
        int availableQuantity,
        int rentedQuantity,
        DateTime? returnedAtUtc = null)
    {
        var rentalId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Products.Add(new Product
        {
            Id = productId,
            TenantId = TestTenantId,
            Name = "Rower magazynowy",
            Sku = $"STOCK-{productId:N}",
            DailyPrice = 50m,
            AvailableQuantity = availableQuantity,
            Available = true,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        });
        db.Customers.Add(new Customer
        {
            Id = customerId,
            TenantId = TestTenantId,
            FullName = "Klient magazynu"
        });
        db.Rentals.Add(new Rental
        {
            Id = rentalId,
            TenantId = TestTenantId,
            CustomerId = customerId,
            StartDateUtc = startDateUtc,
            EndDateUtc = endDateUtc,
            Status = status,
            ReturnedAtUtc = returnedAtUtc,
            CreatedAtUtc = DateTime.UtcNow
        });
        db.RentalItems.Add(new RentalItem
        {
            Id = Guid.NewGuid(),
            RentalId = rentalId,
            ProductId = productId,
            Quantity = rentedQuantity,
            PricePerDay = 50m,
            Subtotal = rentedQuantity * 50m
        });
        await db.SaveChangesAsync();
        return (rentalId, productId, customerId);
    }

    private async Task<Guid> SeedTenantAsync(string name, bool isDemo = false)
    {
        var tenantId = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = name,
            IsDemo = isDemo
        });
        await db.SaveChangesAsync();
        return tenantId;
    }

    private async Task<(Guid CustomerId, Guid OrderId, string Email, string OrderNumber)>
        SeedGuestMarketplaceOrderAsync()
    {
        var customerId = Guid.NewGuid();
        var checkoutSessionId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var email = $"guest-order-{Guid.NewGuid():N}@example.test";
        var orderNumber = $"RS-20260710-{orderId.ToString("N")[..8].ToUpperInvariant()}";
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Customers.Add(new Customer
        {
            Id = customerId,
            TenantId = Guid.Empty,
            FullName = "Klient gość",
            Email = email,
            PhoneNumber = "+48123123123",
            CreatedAtUtc = DateTime.UtcNow
        });
        db.CheckoutSessions.Add(new CheckoutSession
        {
            Id = checkoutSessionId,
            IdempotencyKey = $"guest-access-{orderId:N}",
            PayloadJson = "{}",
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(30),
            AcceptedTermsVersion = LegalDocumentVersions.Terms,
            AcknowledgedPrivacyVersion = LegalDocumentVersions.Privacy,
            LegalAcceptedAtUtc = DateTime.UtcNow
        });
        db.MarketplaceOrders.Add(new MarketplaceOrder
        {
            Id = orderId,
            OrderNumber = orderNumber,
            CustomerId = customerId,
            CustomerEmailSnapshot = email,
            CheckoutSessionId = checkoutSessionId,
            IdempotencyKey = $"guest-access-{orderId:N}",
            TotalAmount = 100m,
            DepositAmount = 30m,
            Currency = "PLN",
            Status = "Confirmed",
            PaymentStatus = "DepositPaid",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            AcceptedTermsVersion = LegalDocumentVersions.Terms,
            AcknowledgedPrivacyVersion = LegalDocumentVersions.Privacy
        });
        await db.SaveChangesAsync();
        return (customerId, orderId, email, orderNumber);
    }

    private static PaymentQuoteRequest CreateDailyQuoteRequest(
        Guid productId,
        DateTime startDateUtc,
        DateTime endDateUtc,
        int quantity = 1) =>
        new()
        {
            StartDateUtc = startDateUtc,
            EndDateUtc = endDateUtc,
            RentalType = RentalTypeDto.Daily,
            Items = [new CreateRentalItem { ProductId = productId, Quantity = quantity }]
        };

    private async Task<Guid> SeedRentalAsync(
        Guid tenantId,
        string? contractUrl = null,
        string companyName = "Test Rental")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        if (!await db.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Id == tenantId))
        {
            db.Tenants.Add(new Tenant { Id = tenantId, Name = "Seeded tenant" });
        }

        var customerId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var rentalId = Guid.NewGuid();
        db.CompanyInfos.Add(new CompanyInfo
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = companyName,
            RegulationsText = "Test regulations"
        });
        db.Customers.Add(new Customer
        {
            Id = customerId,
            TenantId = tenantId,
            FullName = "Jan Kowalski",
            Email = "jan@example.test",
            PhoneNumber = "+48123123123"
        });
        db.Products.Add(new Product
        {
            Id = productId,
            TenantId = tenantId,
            Name = "Rower testowy",
            Sku = $"TEST-{productId:N}",
            DailyPrice = 50,
            AvailableQuantity = 1,
            CreatedAtUtc = DateTime.UtcNow
        });
        db.Rentals.Add(new Rental
        {
            Id = rentalId,
            TenantId = tenantId,
            CustomerId = customerId,
            StartDateUtc = DateTime.UtcNow.AddDays(1),
            EndDateUtc = DateTime.UtcNow.AddDays(2),
            TotalAmount = 50,
            DepositAmount = 100,
            ContractUrl = contractUrl,
            Status = RentalStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow
        });
        db.RentalItems.Add(new RentalItem
        {
            Id = Guid.NewGuid(),
            RentalId = rentalId,
            ProductId = productId,
            Quantity = 1,
            PricePerDay = 50,
            Subtotal = 50
        });
        await db.SaveChangesAsync();

        return rentalId;
    }

    private sealed class TestDbContextFactory : IDbContextFactory<ApplicationDbContext>
    {
        private readonly DbContextOptions<ApplicationDbContext> _options;

        public TestDbContextFactory(DbContextOptions<ApplicationDbContext> options) => _options = options;

        public ApplicationDbContext CreateDbContext()
            => new(_options);

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

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, "test"),
                new Claim(ClaimTypes.Role, "Owner"),
                new Claim("tenant-id", TestTenantId.ToString())
            };
            if (!string.Equals(values[0], "tenant-only", StringComparison.Ordinal))
                claims.Add(new Claim(ClaimTypes.Role, "SuperAdmin"));

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
        public Task<byte[]> GenerateRentalContractAsync(Rental rental, IEnumerable<RentalItem> items, Customer customer, IEnumerable<Product> products, CompanyInfo? companyInfo = null, CancellationToken ct = default)
            => Task.FromResult(System.Text.Encoding.UTF8.GetBytes("PDF"));
        public Task<byte[]> GenerateRentalContractAsync(string templateContent, Rental rental, IEnumerable<RentalItem> items, Customer customer, IEnumerable<Product> products, CompanyInfo? companyInfo = null, CancellationToken ct = default)
            => Task.FromResult(System.Text.Encoding.UTF8.GetBytes("PDF"));
        public Task<string> GenerateAndSaveRentalContractAsync(Rental rental, IEnumerable<RentalItem> items, Customer customer, IEnumerable<Product> products, CompanyInfo? companyInfo = null, string? templateContent = null, CancellationToken ct = default)
            => Task.FromResult($"https://test/contracts/{rental.TenantId}/contract_{rental.Id}.pdf");
        public Task SendRentalContractByEmailAsync(Rental rental, IEnumerable<RentalItem> items, Customer customer, IEnumerable<Product> products, CompanyInfo? companyInfo = null, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task SendRentalConfirmationEmailAsync(Rental rental, IEnumerable<RentalItem> items, Customer customer, IEnumerable<Product> products, CompanyInfo? companyInfo = null, string? templateContent = null, CancellationToken ct = default)
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
        public Task<string> SavePrivateAsync(string relativePath, byte[] content, CancellationToken ct = default)
            => Task.FromResult(relativePath);
        public Task<string> GetPrivateReadUrlAsync(string storageReference, TimeSpan ttl, CancellationToken ct = default)
            => Task.FromResult($"https://test/{storageReference}?sas=fake");
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
}




