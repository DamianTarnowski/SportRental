using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SportRental.Admin.Services.Contracts;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;
using SportRentalShared = SportRental.Shared.Models;

namespace SportRental.Admin.Payments;

/// <summary>
/// Idempotentna finalizacja zamówienia po pomyślnej płatności Stripe — tworzy rental(y),
/// generuje umowę PDF, wysyła email potwierdzenia. Wywoływana z DWÓCH miejsc:
///   1. /api/checkout/finalize-session/{sessionId} — gdy klient wraca z Stripe redirectu
///   2. Stripe webhook 'checkout.session.completed' — gdy klient zamknął kartę przed redirectem
/// IdempotencyKey w metadata sesji Stripe gwarantuje że pierwszy z tych dwóch zwycięża,
/// drugi widzi `existingRental != null` i wraca success bez duplikacji.
/// </summary>
public sealed class CheckoutFinalizationService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
    private readonly IContractGenerator _contracts;
    private readonly StripeOptions _stripe;
    private readonly ILogger<CheckoutFinalizationService> _logger;

    public CheckoutFinalizationService(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IContractGenerator contracts,
        IOptions<StripeOptions> stripeOptions,
        ILogger<CheckoutFinalizationService> logger)
    {
        _dbFactory = dbFactory;
        _contracts = contracts;
        _stripe = stripeOptions.Value;
        _logger = logger;
    }

    public async Task<SportRentalShared.FinalizeSessionResponse> FinalizeAsync(
        string stripeSessionId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_stripe.SecretKey))
            return new SportRentalShared.FinalizeSessionResponse(false, "Stripe is not configured.", null);

        Stripe.StripeConfiguration.ApiKey = _stripe.SecretKey;

        try
        {
            var sessionService = new Stripe.Checkout.SessionService();
            var session = await sessionService.GetAsync(stripeSessionId, cancellationToken: ct);

            if (session.PaymentStatus != "paid")
                return new SportRentalShared.FinalizeSessionResponse(false, $"Payment not completed. Status: {session.PaymentStatus}", null);

            // Idempotency check — czy już utworzony rental?
            if (session.Metadata.TryGetValue("idempotency_key", out var idempotencyKey))
            {
                await using var checkDb = await _dbFactory.CreateDbContextAsync(ct);
                var existingRental = await checkDb.Rentals.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(r => r.IdempotencyKey == idempotencyKey, ct);
                if (existingRental != null)
                    return new SportRentalShared.FinalizeSessionResponse(true, "Rental already created.", existingRental.Id);
            }

            // Pobierz payload z DB (zostawiony przy create-session)
            if (!session.Metadata.TryGetValue("checkout_session_id", out var checkoutSessionIdStr)
                || !Guid.TryParse(checkoutSessionIdStr, out var checkoutSessionId))
            {
                return new SportRentalShared.FinalizeSessionResponse(false, "Missing checkout_session_id in metadata.", null);
            }

            await using var payloadDb = await _dbFactory.CreateDbContextAsync(ct);
            var checkoutSession = await payloadDb.CheckoutSessions
                .FirstOrDefaultAsync(cs => cs.Id == checkoutSessionId, ct);
            if (checkoutSession == null)
                return new SportRentalShared.FinalizeSessionResponse(false, "Checkout session not found.", null);

            var payload = System.Text.Json.JsonSerializer.Deserialize<CheckoutRentalPayload>(checkoutSession.PayloadJson);
            if (payload == null)
                return new SportRentalShared.FinalizeSessionResponse(false, "Invalid checkout payload.", null);

            checkoutSession.IsProcessed = true;
            checkoutSession.StripeSessionId = stripeSessionId;
            await payloadDb.SaveChangesAsync(ct);

            Guid? firstRentalId = null;
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            foreach (var tenantPayload in payload.Tenants)
            {
                db.SetTenant(tenantPayload.TenantId);

                var rental = new Rental
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantPayload.TenantId,
                    CustomerId = payload.Customer.CustomerId,
                    StartDateUtc = payload.StartDateUtc,
                    EndDateUtc = payload.EndDateUtc,
                    TotalAmount = tenantPayload.TotalAmount,
                    DepositAmount = tenantPayload.DepositAmount,
                    Status = RentalStatus.Confirmed,
                    PaymentStatus = "DepositPaid",
                    PaymentIntentId = session.PaymentIntentId,
                    IdempotencyKey = payload.IdempotencyKey,
                    Notes = payload.Notes,
                    Source = RentalSource.Online,
                    CreatedAtUtc = DateTime.UtcNow
                };

                foreach (var item in tenantPayload.Items)
                {
                    var product = await db.Products.IgnoreQueryFilters()
                        .FirstOrDefaultAsync(p => p.Id == item.ProductId, ct);
                    if (product != null)
                    {
                        rental.Items.Add(new RentalItem
                        {
                            Id = Guid.NewGuid(),
                            ProductId = item.ProductId,
                            Quantity = item.Quantity,
                            PricePerDay = product.DailyPrice
                        });
                    }
                }

                db.Rentals.Add(rental);
                await db.SaveChangesAsync(ct);
                firstRentalId ??= rental.Id;

                // Generowanie umowy + email — best-effort, nie przerywaj flow przy błędzie.
                try
                {
                    var customer = await db.Customers.IgnoreQueryFilters()
                        .FirstOrDefaultAsync(c => c.Id == rental.CustomerId, ct);
                    var productIds = rental.Items.Select(i => i.ProductId).ToList();
                    var products = await db.Products.IgnoreQueryFilters()
                        .Where(p => productIds.Contains(p.Id)).ToListAsync(ct);
                    var companyInfo = await db.CompanyInfos.IgnoreQueryFilters()
                        .FirstOrDefaultAsync(ci => ci.TenantId == tenantPayload.TenantId, ct);

                    if (customer != null)
                    {
                        var contractUrl = await _contracts.GenerateAndSaveRentalContractAsync(
                            rental, rental.Items, customer, products, companyInfo, ct);
                        rental.ContractUrl = contractUrl;
                        await db.SaveChangesAsync(ct);

                        if (!string.IsNullOrWhiteSpace(customer.Email))
                        {
                            await _contracts.SendRentalConfirmationEmailAsync(
                                rental, rental.Items, customer, products, companyInfo, ct);
                            rental.IsEmailSent = true;
                            await db.SaveChangesAsync(ct);
                            _logger.LogInformation("Email z umową wysłany do {Email} dla wynajmu {RentalId}", customer.Email, rental.Id);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Generowanie umowy/emaila nie powiodło się dla wynajmu {RentalId}", rental.Id);
                }
            }

            return new SportRentalShared.FinalizeSessionResponse(true, "Rental created successfully.", firstRentalId);
        }
        catch (Stripe.StripeException ex)
        {
            _logger.LogError(ex, "Stripe error during finalize {SessionId}", stripeSessionId);
            return new SportRentalShared.FinalizeSessionResponse(false, $"Stripe error: {ex.Message}", null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during finalize {SessionId}", stripeSessionId);
            return new SportRentalShared.FinalizeSessionResponse(false, $"Unexpected error: {ex.Message}", null);
        }
    }
}
