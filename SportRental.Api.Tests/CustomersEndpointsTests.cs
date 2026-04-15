using System.IO;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.TestHost;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;
using SportRental.Shared.Models;
using Xunit;
using Xunit.Abstractions;

namespace SportRental.Api.Tests;

public class CustomersEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly ITestOutputHelper _output;
    private readonly string _databasePath;

    public CustomersEndpointsTests(WebApplicationFactory<Program> factory, ITestOutputHelper output)
    {
        _output = output;
        _databasePath = Path.Combine(Path.GetTempPath(), $"customers-tests-{Guid.NewGuid():N}.db");

        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Remove all EF Core related services (including DbContextPool)
                // Remove by finding all descriptors related to ApplicationDbContext
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
                services.AddDbContext<ApplicationDbContext>(options =>
                    options.UseSqlite($"Data Source={_databasePath}"));
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
    public async Task CustomerCrudFlow_Works()
    {
        var tenantId = Guid.NewGuid();
        await PrepareDatabaseAsync(tenantId);

        using var client = _factory.CreateClient();
        TestApiClientHelper.AuthenticateClient(client, tenantId);

        var createRequest = new CreateCustomerRequest
        {
            FullName = "Jan Kowalski",
            Email = "jan@example.com",
            PhoneNumber = "+48123123123",
            Address = "Testowa 1",
            DocumentNumber = "ABC123456",
            Notes = "Prefer night delivery"
        };

        var createResponse = await client.PostAsJsonAsync("/api/customers", createRequest);
        if (!createResponse.IsSuccessStatusCode)
        {
            var body = await createResponse.Content.ReadAsStringAsync();
            _output.WriteLine(body);
        }

        createResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<CustomerDto>();
        created.Should().NotBeNull();
        created!.FullName.Should().Be(createRequest.FullName);
        created.Email.Should().Be(createRequest.Email);

        // Re-auth as the created customer (IDOR-safe endpoints below require customer-id claim matching the target id)
        TestApiClientHelper.AuthenticateAsCustomer(client, tenantId, created.Id, created.Email);

        var lookupResponse = await client.GetAsync($"/api/customers/by-email?email={Uri.EscapeDataString(createRequest.Email)}");
        if (!lookupResponse.IsSuccessStatusCode)
        {
            var body = await lookupResponse.Content.ReadAsStringAsync();
            _output.WriteLine(body);
        }

        lookupResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var lookedUp = await lookupResponse.Content.ReadFromJsonAsync<CustomerDto>();
        lookedUp.Should().NotBeNull();
        lookedUp!.Id.Should().Be(created.Id);

        var updateRequest = new CreateCustomerRequest
        {
            FullName = "Jan Kowalski",
            Email = "jan@example.com",
            PhoneNumber = "+48999111222",
            Address = "Nowa 2",
            DocumentNumber = "XYZ987654",
            Notes = "Updated notes"
        };

        var updateResponse = await client.PutAsJsonAsync($"/api/customers/{created.Id}", updateRequest);
        if (!updateResponse.IsSuccessStatusCode)
        {
            var body = await updateResponse.Content.ReadAsStringAsync();
            _output.WriteLine(body);
        }

        updateResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var updated = await updateResponse.Content.ReadFromJsonAsync<CustomerDto>();
        updated.Should().NotBeNull();
        updated!.PhoneNumber.Should().Be(updateRequest.PhoneNumber);
        updated.Address.Should().Be(updateRequest.Address);

        var byIdResponse = await client.GetAsync($"/api/customers/{created.Id}");
        byIdResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var byId = await byIdResponse.Content.ReadFromJsonAsync<CustomerDto>();
        byId.Should().NotBeNull();
        byId!.PhoneNumber.Should().Be(updateRequest.PhoneNumber);
        byId.Notes.Should().Be(updateRequest.Notes);
    }

    [Fact]
    public async Task CreateCustomer_AllowsAnonymous_ButRejectsDuplicateEmail()
    {
        var tenantId = Guid.NewGuid();
        await PrepareDatabaseAsync(tenantId);

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId.ToString());

        var response = await client.PostAsJsonAsync("/api/customers", new CreateCustomerRequest
        {
            FullName = "Anon",
            Email = "anon@example.com"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<CustomerDto>();
        created.Should().NotBeNull();
        created!.Email.Should().Be("anon@example.com");

        // Duplicate email: must not leak the existing record — security fix SEC-A03.
        var duplicate = await client.PostAsJsonAsync("/api/customers", new CreateCustomerRequest
        {
            FullName = "Impostor",
            Email = "anon@example.com"
        });
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task GetCustomer_BlocksCrossCustomerAccess()
    {
        var tenantId = Guid.NewGuid();
        await PrepareDatabaseAsync(tenantId);

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId.ToString());

        var victim = await client.PostAsJsonAsync("/api/customers", new CreateCustomerRequest
        {
            FullName = "Victim",
            Email = "victim@example.com"
        });
        victim.StatusCode.Should().Be(HttpStatusCode.Created);
        var victimDto = await victim.Content.ReadFromJsonAsync<CustomerDto>();
        victimDto.Should().NotBeNull();

        // Attacker authenticates with their own customer-id but tries to read the victim.
        TestApiClientHelper.AuthenticateAsCustomer(client, tenantId, Guid.NewGuid(), "attacker@example.com");

        var attempt = await client.GetAsync($"/api/customers/{victimDto!.Id}");
        attempt.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var lookup = await client.GetAsync($"/api/customers/by-email?email={Uri.EscapeDataString(victimDto.Email!)}");
        lookup.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task PrepareDatabaseAsync(Guid tenantId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        if (!await db.Tenants.AnyAsync(t => t.Id == tenantId))
        {
            db.Tenants.Add(new Tenant
            {
                Id = tenantId,
                Name = "Test Tenant",
                CreatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }
    }
}
