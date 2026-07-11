using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SportRental.Admin;
using SportRental.Admin.Payments;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;
using SportRental.Shared.Models;

namespace SportRental.Admin.Tests.Api;

public sealed class RentalCancellationRefundTests : IDisposable
{
    private static readonly Guid TenantId = Guid.Parse("8c8fb513-4c6f-46b9-84a2-d0cb72b875f3");

    private readonly RefundWebApplicationFactory _factory = new();

    [Fact]
    public async Task DeletePaidRental_WhenRefundSucceeds_RefundsDepositBeforeCancellingRental()
    {
        var client = CreateAuthenticatedClient();
        var customerId = await CreateGuestSessionAsync(client);
        var rentalId = await SeedPaidRentalAsync(customerId);
        _factory.PaymentGateway.RefundResult = true;

        var response = await client.DeleteAsync($"/api/rentals/{rentalId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var refund = Assert.Single(_factory.PaymentGateway.RefundCalls);
        Assert.Equal("pi_refund_success", refund.PaymentIntentId);
        Assert.Equal(149.90m, refund.Amount);
        Assert.Equal("requested_by_customer", refund.Reason);
        Assert.Equal($"cancel-rental:{rentalId:N}", refund.IdempotencyKey);

        var rental = await LoadRentalAsync(rentalId);
        Assert.Equal(RentalStatus.Cancelled, rental.Status);
        Assert.Equal("DepositRefunded", rental.PaymentStatus);
        Assert.Equal(0m, rental.PaidAmount);
        Assert.Null(rental.DepositPaidAtUtc);
    }

    [Fact]
    public async Task DeletePaidRental_WhenRefundFails_ReturnsBadGatewayAndLeavesRentalUnchanged()
    {
        var client = CreateAuthenticatedClient();
        var customerId = await CreateGuestSessionAsync(client);
        var rentalId = await SeedPaidRentalAsync(customerId, paymentIntentId: "pi_refund_failure");
        _factory.PaymentGateway.RefundResult = false;

        var response = await client.DeleteAsync($"/api/rentals/{rentalId}");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var refund = Assert.Single(_factory.PaymentGateway.RefundCalls);
        Assert.Equal("pi_refund_failure", refund.PaymentIntentId);
        Assert.Equal(149.90m, refund.Amount);

        var rental = await LoadRentalAsync(rentalId);
        Assert.Equal(RentalStatus.Confirmed, rental.Status);
        Assert.Equal("DepositPaid", rental.PaymentStatus);
        Assert.Equal(0m, rental.PaidAmount);
        Assert.NotNull(rental.DepositPaidAtUtc);
    }

    [Fact]
    public async Task DeleteMarketplaceRentals_UpdatesParentAfterEachPartialRefund()
    {
        var client = CreateAuthenticatedClient();
        var customerId = await CreateGuestSessionAsync(client);
        var seeded = await SeedMarketplaceOrderAsync(customerId);
        _factory.PaymentGateway.RefundResult = true;

        var firstResponse = await client.DeleteAsync($"/api/rentals/{seeded.FirstRentalId}");

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        var afterFirst = await LoadMarketplaceOrderAsync(seeded.OrderId);
        Assert.Equal(60m, afterFirst.RefundedDepositAmount);
        Assert.Equal("PartiallyRefunded", afterFirst.PaymentStatus);
        Assert.Equal("PartiallyCancelled", afterFirst.Status);

        var secondResponse = await client.DeleteAsync($"/api/rentals/{seeded.SecondRentalId}");

        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        var afterSecond = await LoadMarketplaceOrderAsync(seeded.OrderId);
        Assert.Equal(100m, afterSecond.RefundedDepositAmount);
        Assert.Equal("Refunded", afterSecond.PaymentStatus);
        Assert.Equal("Cancelled", afterSecond.Status);
        Assert.Collection(
            _factory.PaymentGateway.RefundCalls,
            call => Assert.Equal($"cancel-rental:{seeded.FirstRentalId:N}", call.IdempotencyKey),
            call => Assert.Equal($"cancel-rental:{seeded.SecondRentalId:N}", call.IdempotencyKey));
    }

    private HttpClient CreateAuthenticatedClient()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Add("X-Tenant-Id", TenantId.ToString());
        return client;
    }

    private static async Task<Guid> CreateGuestSessionAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/auth/guest-session", new
        {
            Email = $"refund-{Guid.NewGuid():N}@example.test",
            FullName = "Klient zwrotu",
            PhoneNumber = "+48123123123",
            Address = (string?)null,
            DocumentNumber = (string?)null,
            Notes = (string?)null
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(payload.TryGetProperty("customerId", out var customerIdElement));
        Assert.True(customerIdElement.TryGetGuid(out var customerId));
        return customerId;
    }

    private async Task<Guid> SeedPaidRentalAsync(
        Guid customerId,
        string paymentIntentId = "pi_refund_success")
    {
        var rentalId = Guid.NewGuid();
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Rentals.Add(new Rental
        {
            Id = rentalId,
            TenantId = TenantId,
            CustomerId = customerId,
            StartDateUtc = DateTime.UtcNow.AddDays(2),
            EndDateUtc = DateTime.UtcNow.AddDays(4),
            Status = RentalStatus.Confirmed,
            TotalAmount = 499.67m,
            DepositAmount = 149.90m,
            PaidAmount = 0m,
            PaymentIntentId = paymentIntentId,
            PaymentStatus = "DepositPaid",
            PaidAtUtc = DateTime.UtcNow.AddMinutes(-5),
            DepositPaidAtUtc = DateTime.UtcNow.AddMinutes(-5),
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-10)
        });
        await db.SaveChangesAsync();
        return rentalId;
    }

    private async Task<Rental> LoadRentalAsync(Guid rentalId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Rentals
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(r => r.Id == rentalId);
    }

    private async Task<(Guid OrderId, Guid FirstRentalId, Guid SecondRentalId)> SeedMarketplaceOrderAsync(
        Guid customerId)
    {
        var orderId = Guid.NewGuid();
        var checkoutSessionId = Guid.NewGuid();
        var firstRentalId = Guid.NewGuid();
        var secondRentalId = Guid.NewGuid();
        var secondTenantId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Tenants.Add(new Tenant { Id = secondTenantId, Name = "Drugi tenant zwrotu" });
        db.CheckoutSessions.Add(new CheckoutSession
        {
            Id = checkoutSessionId,
            IdempotencyKey = $"marketplace-refund-{orderId:N}",
            PayloadJson = "{}",
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(30)
        });
        db.MarketplaceOrders.Add(new MarketplaceOrder
        {
            Id = orderId,
            OrderNumber = $"RS-20260710-{orderId.ToString("N")[..8].ToUpperInvariant()}",
            CustomerId = customerId,
            CustomerEmailSnapshot = "marketplace-refund@example.test",
            CheckoutSessionId = checkoutSessionId,
            IdempotencyKey = $"marketplace-refund-{orderId:N}",
            PaymentIntentId = "pi_marketplace_refund",
            TotalAmount = 400m,
            DepositAmount = 100m,
            Currency = "PLN",
            Status = "Confirmed",
            PaymentStatus = "DepositPaid",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            PaidAtUtc = now
        });
        db.Rentals.AddRange(
            MarketplaceRental(firstRentalId, orderId, customerId, TenantId, 1, 60m, now),
            MarketplaceRental(secondRentalId, orderId, customerId, secondTenantId, 2, 40m, now));
        await db.SaveChangesAsync();
        return (orderId, firstRentalId, secondRentalId);
    }

    private static Rental MarketplaceRental(
        Guid rentalId,
        Guid orderId,
        Guid customerId,
        Guid tenantId,
        int sequence,
        decimal deposit,
        DateTime now) => new()
    {
        Id = rentalId,
        TenantId = tenantId,
        CustomerId = customerId,
        MarketplaceOrderId = orderId,
        OrderSequence = sequence,
        StartDateUtc = now.AddDays(2),
        EndDateUtc = now.AddDays(4),
        Status = RentalStatus.Confirmed,
        TotalAmount = 200m,
        DepositAmount = deposit,
        PaymentIntentId = "pi_marketplace_refund",
        PaymentStatus = "DepositPaid",
        DepositPaidAtUtc = now,
        CreatedAtUtc = now
    };

    private async Task<MarketplaceOrder> LoadMarketplaceOrderAsync(Guid orderId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.MarketplaceOrders.AsNoTracking()
            .SingleAsync(order => order.Id == orderId);
    }

    public void Dispose() => _factory.Dispose();

    private sealed class RefundWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly string _databaseName = $"rental-refund-tests-{Guid.NewGuid():N}";

        public FakePaymentGateway PaymentGateway { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureTestServices(services =>
            {
                var dbDescriptors = services
                    .Where(d =>
                        d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>) ||
                        d.ServiceType == typeof(IDbContextFactory<ApplicationDbContext>) ||
                        (d.ServiceType.IsGenericType &&
                         d.ServiceType.GetGenericArguments().Contains(typeof(ApplicationDbContext))) ||
                        (d.ServiceType.FullName?.StartsWith("Microsoft.EntityFrameworkCore.Internal") ?? false))
                    .Concat(services.Where(d => d.ServiceType == typeof(ApplicationDbContext)))
                    .Distinct()
                    .ToList();

                foreach (var descriptor in dbDescriptors)
                {
                    services.Remove(descriptor);
                }

                services.AddDbContext<ApplicationDbContext>(options => options
                    .UseInMemoryDatabase(_databaseName)
                    .ConfigureWarnings(warnings =>
                        warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
                services.AddScoped<IDbContextFactory<ApplicationDbContext>, RefundDbContextFactory>();

                services.RemoveAll<IPaymentGateway>();
                services.AddSingleton<IPaymentGateway>(PaymentGateway);
            });
        }

        protected override void ConfigureClient(HttpClient client)
        {
            base.ConfigureClient(client);
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Database.EnsureCreated();
            if (!db.Tenants.IgnoreQueryFilters().Any(t => t.Id == TenantId))
            {
                db.Tenants.Add(new Tenant { Id = TenantId, Name = "Tenant zwrotów" });
                db.SaveChanges();
            }
        }
    }

    private sealed class RefundDbContextFactory : IDbContextFactory<ApplicationDbContext>
    {
        private readonly DbContextOptions<ApplicationDbContext> _options;

        public RefundDbContextFactory(DbContextOptions<ApplicationDbContext> options)
            => _options = options;

        public ApplicationDbContext CreateDbContext() => new(_options);

        public ValueTask<ApplicationDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(CreateDbContext());
    }

    private sealed class FakePaymentGateway : IPaymentGateway
    {
        public bool RefundResult { get; set; }
        public List<RefundCall> RefundCalls { get; } = [];

        public Task<bool> RefundPaymentAsync(
            Guid tenantId,
            string id,
            decimal? amount = null,
            string? reason = null,
            string? idempotencyKey = null)
        {
            RefundCalls.Add(new RefundCall(tenantId, id, amount, reason, idempotencyKey));
            return Task.FromResult(RefundResult);
        }

        public Task<PaymentIntentDto> CreatePaymentIntentAsync(
            Guid tenantId,
            decimal amount,
            decimal depositAmount,
            string currency,
            Dictionary<string, string>? metadata = null)
            => throw new NotSupportedException();

        public Task<PaymentIntentDto?> GetPaymentIntentAsync(Guid tenantId, string id)
            => throw new NotSupportedException();

        public Task<bool> CapturePaymentAsync(Guid tenantId, string id)
            => throw new NotSupportedException();

        public Task<bool> CancelPaymentAsync(Guid tenantId, string id)
            => throw new NotSupportedException();
    }

    private sealed record RefundCall(
        Guid TenantId,
        string PaymentIntentId,
        decimal? Amount,
        string? Reason,
        string? IdempotencyKey);
}
