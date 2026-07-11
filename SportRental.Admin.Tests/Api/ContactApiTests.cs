using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SportRental.Admin.Services.Email;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;
using SportRental.Shared.Models;

namespace SportRental.Admin.Tests.Api;

public sealed class ContactApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _baseFactory;

    public ContactApiTests(WebApplicationFactory<Program> baseFactory)
    {
        _baseFactory = baseFactory;
    }

    [Fact]
    public async Task Contact_ValidMessage_SendsEncodedEmailToTenantAddress()
    {
        var sender = new RecordingEmailSender();
        await using var factory = CreateFactory(sender);
        var tenantId = await SeedTenantAsync(factory, isDemo: false, email: "punkt@example.com");
        var client = factory.CreateClient();
        var request = ValidRequest(tenantId);
        request.Name = "Jan <script>alert(1)</script>";
        request.Subject = "Pytanie\r\no sprzęt";
        request.Message = "Czy mogę wypożyczyć <b>rower</b> w sobotę?";

        var response = await client.PostAsJsonAsync("/api/contact", request);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var email = sender.Sent.Should().ContainSingle().Subject;
        email.Recipient.Should().Be("punkt@example.com");
        email.Subject.Should().Be("[RentSpot] Pytanie  o sprzęt");
        email.Html.Should().Contain("Jan &lt;script&gt;alert(1)&lt;/script&gt;");
        email.Html.Should().Contain("&lt;b&gt;rower&lt;/b&gt;");
        email.Html.Should().NotContain("<script>");
    }

    [Fact]
    public async Task Contact_DemoTenant_AcceptsButDoesNotInvokeEmailSender()
    {
        var sender = new RecordingEmailSender();
        await using var factory = CreateFactory(sender);
        var tenantId = await SeedTenantAsync(factory, isDemo: true, email: "demo@example.com");
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/contact", ValidRequest(tenantId));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        sender.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Contact_InvalidPayload_ReturnsValidationProblemWithoutSending()
    {
        var sender = new RecordingEmailSender();
        await using var factory = CreateFactory(sender);
        var client = factory.CreateClient();
        var request = new ContactMessageRequest
        {
            TenantId = Guid.Empty,
            Name = "A",
            Email = "nie-email",
            Subject = "x",
            Message = "krótka"
        };

        var response = await client.PostAsJsonAsync("/api/contact", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        sender.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Contact_TenantWithoutEmail_ReturnsUnprocessableEntityWithoutSending()
    {
        var sender = new RecordingEmailSender();
        await using var factory = CreateFactory(sender);
        var tenantId = await SeedTenantAsync(factory, isDemo: false, email: null);
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/contact", ValidRequest(tenantId));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        sender.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Contact_WhenSmtpIsDisabled_ReturnsServiceUnavailableWithoutFalseSuccess()
    {
        var sender = new RecordingEmailSender();
        await using var factory = CreateFactory(sender, emailEnabled: false);
        var tenantId = await SeedTenantAsync(factory, isDemo: false, email: "punkt@example.com");
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/contact", ValidRequest(tenantId));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        sender.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task TenantLocations_IncludesContactOnlyTenant_ReportsProductCount_AndExcludesDemo()
    {
        var sender = new RecordingEmailSender();
        await using var factory = CreateFactory(sender);
        var regularTenantId = await SeedTenantAsync(factory, isDemo: false, email: "punkt@example.com");
        var contactOnlyTenantId = await SeedTenantAsync(factory, isDemo: false, email: "kontakt@example.com");
        var demoTenantId = await SeedTenantAsync(factory, isDemo: true, email: "demo@example.com");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tomorrow = DateOnly.FromDateTime(DateTime.Today.AddDays(1));
            db.Products.Add(new Product
            {
                Id = Guid.NewGuid(),
                TenantId = regularTenantId,
                Name = "Narty testowe",
                Sku = "MAP-1",
                DailyPrice = 100m,
                AvailableQuantity = 1
            });
            db.BusinessHoursSchedules.Add(new BusinessHoursSchedule
            {
                Id = Guid.NewGuid(),
                TenantId = regularTenantId,
                Days =
                [
                    new BusinessHoursDay
                    {
                        Id = Guid.NewGuid(),
                        DayOfWeek = tomorrow.DayOfWeek,
                        OpenFrom = new TimeOnly(9, 0),
                        OpenTo = new TimeOnly(17, 0)
                    }
                ]
            });
            db.BusinessHoursExceptions.Add(new BusinessHoursException
            {
                Id = Guid.NewGuid(),
                TenantId = regularTenantId,
                Date = tomorrow,
                IsClosed = true,
                Reason = "Święto"
            });
            await db.SaveChangesAsync();
        }
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/tenants/locations");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var locations = await response.Content.ReadFromJsonAsync<List<TenantLocationDto>>();
        var regular = locations.Should().ContainSingle(location => location.TenantId == regularTenantId).Subject;
        regular.ProductCount.Should().Be(1);
        regular.OpeningHours.Should().Contain("09:00–17:00");
        regular.OpeningHours.Should().Contain("Wyjątki:");
        regular.OpeningHours.Should().Contain("zamknięte");
        regular.OpeningHours.Should().NotContain("NIEAKTUALNE");
        locations.Should().ContainSingle(location =>
            location.TenantId == contactOnlyTenantId && location.ProductCount == 0);
        locations.Should().NotContain(location => location.TenantId == demoTenantId);
    }

    private WebApplicationFactory<Program> CreateFactory(
        RecordingEmailSender sender,
        bool emailEnabled = true)
    {
        var databaseName = $"contact-api-{Guid.NewGuid():N}";
        return _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Email:Smtp:Enabled"] = emailEnabled.ToString()
                }));
            builder.ConfigureTestServices(services =>
            {
                var dbDescriptors = services
                    .Where(descriptor =>
                        descriptor.ServiceType == typeof(DbContextOptions<ApplicationDbContext>) ||
                        descriptor.ServiceType == typeof(IDbContextFactory<ApplicationDbContext>) ||
                        descriptor.ServiceType == typeof(ApplicationDbContext) ||
                        (descriptor.ServiceType.IsGenericType &&
                         descriptor.ServiceType.GetGenericArguments().Contains(typeof(ApplicationDbContext))) ||
                        (descriptor.ServiceType.FullName?.StartsWith("Microsoft.EntityFrameworkCore.Internal") ?? false))
                    .ToList();

                foreach (var descriptor in dbDescriptors)
                {
                    services.Remove(descriptor);
                }

                services.AddDbContext<ApplicationDbContext>(options => options
                    .UseInMemoryDatabase(databaseName)
                    .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
                services.AddScoped<IDbContextFactory<ApplicationDbContext>, TestDbContextFactory>();

                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender>(sender);
            });
        });
    }

    private static async Task<Guid> SeedTenantAsync(
        WebApplicationFactory<Program> factory,
        bool isDemo,
        string? email)
    {
        var tenantId = Guid.NewGuid();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = isDemo ? "Wypożyczalnia demo" : "Wypożyczalnia testowa",
            IsDemo = isDemo
        });
        db.CompanyInfos.Add(new CompanyInfo
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Punkt testowy",
            Email = email,
            OpeningHours = "NIEAKTUALNE"
        });
        await db.SaveChangesAsync();
        return tenantId;
    }

    private static ContactMessageRequest ValidRequest(Guid tenantId) => new()
    {
        TenantId = tenantId,
        Name = "Jan Kowalski",
        Email = "jan@example.com",
        Phone = "+48 123 456 789",
        Subject = "Pytanie o sprzęt",
        Message = "Czy wybrany sprzęt jest dostępny w sobotę?"
    };

    private sealed class TestDbContextFactory(
        DbContextOptions<ApplicationDbContext> options) : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);

        public ValueTask<ApplicationDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default) => new(CreateDbContext());
    }

    private sealed class RecordingEmailSender : IEmailSender
    {
        public List<SentEmail> Sent { get; } = [];

        public Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            Sent.Add(new SentEmail(email, subject, htmlMessage));
            return Task.CompletedTask;
        }

        public Task SendEmailWithAttachmentAsync(
            string email,
            string subject,
            string htmlMessage,
            string? attachmentPath = null) => Task.CompletedTask;

        public Task SendRentalContractAsync(
            string email,
            string customerName,
            byte[] contractPdf) => Task.CompletedTask;

        public Task SendReminderAsync(
            string email,
            string customerName,
            string reminderText) => Task.CompletedTask;

        public Task SendReturnThankYouAsync(
            string email,
            string customerName,
            string? reviewUrl,
            string? optOutUrl,
            string? companyName) => Task.CompletedTask;
    }

    private sealed record SentEmail(string Recipient, string Subject, string Html);
}
