using System.IO;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SportRental.Infrastructure.Data;
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
        });
    }

    [Fact]
    public async Task CustomerCrudFlow_Works()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.EnsureDeletedAsync();
            await db.Database.EnsureCreatedAsync();
        }

        using var client = _factory.CreateClient();
        var tenantId = Guid.NewGuid();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId.ToString());

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
}
