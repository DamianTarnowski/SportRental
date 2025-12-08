using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SportRental.Api.Services.Email;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;
using SportRental.Shared.Models;
using Stripe;
using Stripe.Checkout;
using DomainCustomer = SportRental.Infrastructure.Domain.Customer;
using DomainProduct = SportRental.Infrastructure.Domain.Product;

namespace SportRental.Api.Payments;

public static class StripeWebhookEndpoints
{
    private const string TenantMetadataKey = "tenant_id";
    private const string CheckoutPayloadMetadataKey = "checkout_payload";
    private const string IdempotencyMetadataKey = "idempotency_key";
    private static readonly JsonSerializerOptions PayloadSerializerOptions = new(JsonSerializerDefaults.Web);

    public static void MapStripeWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/webhooks/stripe", HandleStripeWebhook)
            .WithName("StripeWebhook")
            .WithTags("Webhooks")
            .WithSummary("Stripe webhook handler")
            .WithDescription("Obsługuje webhooks od Stripe (Checkout, PaymentIntent, Refund)")
            .ExcludeFromDescription(); // tylko Stripe powinien wywoływać ten endpoint
    }

    private static async Task<IResult> HandleStripeWebhook(
        HttpRequest request,
        IOptions<StripeOptions> stripeOptions,
        ApplicationDbContext db,
        RentalConfirmationEmailService emailService,
        ILogger<Program> logger)
    {
        var json = await new StreamReader(request.Body).ReadToEndAsync();
        if (string.IsNullOrWhiteSpace(json))
        {
            logger.LogWarning("Stripe webhook payload is empty");
            return Results.BadRequest(new { error = "Payload is empty" });
        }

        try
        {
            var webhookSecret = stripeOptions.Value.WebhookSecret;
            var signatureHeader = request.Headers["Stripe-Signature"].FirstOrDefault();
            Event stripeEvent;

            try
            {
                if (!string.IsNullOrWhiteSpace(webhookSecret) && !string.IsNullOrWhiteSpace(signatureHeader))
                {
                    stripeEvent = EventUtility.ConstructEvent(json, signatureHeader, webhookSecret);
                }
                else
                {
                    logger.LogWarning("Stripe webhook secret not configured. Accepting payload without signature verification (development only).");
                    stripeEvent = EventUtility.ParseEvent(json);
                }
            }
            catch (StripeException ex)
            {
                logger.LogWarning(ex, "Stripe webhook signature verification failed");
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException)
            {
                logger.LogWarning(ex, "Invalid Stripe webhook payload");
                return Results.BadRequest(new { error = "Invalid payload" });
            }

            logger.LogInformation("Stripe webhook received: {EventType}", stripeEvent.Type);

            switch (stripeEvent.Type)
            {
                case "checkout.session.completed":
                    await HandleCheckoutSessionCompletedAsync(stripeEvent, db, emailService, logger);
                    break;

                case "payment_intent.succeeded":
                    await HandlePaymentStatusChangeAsync(stripeEvent, db, PaymentIntentStatus.Succeeded, RentalStatus.Confirmed, logger);
                    break;

                case "payment_intent.payment_failed":
                    await HandlePaymentStatusChangeAsync(stripeEvent, db, PaymentIntentStatus.Failed, RentalStatus.Pending, logger);
                    break;

                case "payment_intent.canceled":
                    await HandlePaymentStatusChangeAsync(stripeEvent, db, PaymentIntentStatus.Canceled, RentalStatus.Cancelled, logger);
                    break;

                case "charge.refunded":
                    await HandleRefundAsync(stripeEvent, db, logger);
                    break;

                default:
                    logger.LogInformation("Unhandled Stripe event type: {EventType}", stripeEvent.Type);
                    break;
            }

            return Results.Ok(new { received = true });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error processing Stripe webhook");
            return Results.BadRequest(new { error = "Unable to process webhook payload" });
        }
    }

    private static async Task HandleCheckoutSessionCompletedAsync(
        Event stripeEvent,
        ApplicationDbContext db,
        RentalConfirmationEmailService emailService,
        ILogger logger)
    {
        if (stripeEvent.Data.Object is not Session session)
        {
            logger.LogWarning("checkout.session.completed payload missing Session object");
            return;
        }

        var metadata = session.Metadata ?? new Dictionary<string, string>();

        var encodedPayload = TryReadPayloadMetadata(metadata, logger, session.Id);
        if (encodedPayload is null)
        {
            return;
        }

        var payload = DecodePayload(encodedPayload, logger);
        if (payload is null)
        {
            logger.LogWarning("Checkout session {SessionId} payload could not be decoded", session.Id);
            return;
        }

        payload = payload with { Tenants = payload.Tenants ?? new List<CheckoutTenantPayload>() };

        if (payload.Tenants.Count == 0)
        {
            logger.LogWarning("Checkout session {SessionId} missing tenant information", session.Id);
            return;
        }

        var paymentIntentId = session.PaymentIntentId ?? session.Id;
        await CreateRentalsFromPayloadAsync(db, emailService, logger, payload, paymentIntentId, session.Id);
    }

    private static string? TryReadPayloadMetadata(
        IDictionary<string, string> metadata,
        ILogger logger,
        string sessionId)
    {
        if (metadata.TryGetValue(CheckoutPayloadMetadataKey, out var encodedPayload))
        {
            return encodedPayload;
        }

        if (metadata.TryGetValue($"{CheckoutPayloadMetadataKey}_parts", out var partsRaw) &&
            int.TryParse(partsRaw, out var parts) &&
            parts > 0)
        {
            var pieces = new List<string>(parts);
            for (var i = 0; i < parts; i++)
            {
                if (metadata.TryGetValue($"{CheckoutPayloadMetadataKey}_{i}", out var chunk))
                {
                    pieces.Add(chunk);
                }
            }

            if (pieces.Count == parts)
            {
                return string.Concat(pieces);
            }

            logger.LogWarning("Checkout session {SessionId} missing some payload chunks", sessionId);
        }

        logger.LogWarning("Checkout session {SessionId} missing payload metadata", sessionId);
        return null;
    }

    private static async Task CreateRentalsFromPayloadAsync(
        ApplicationDbContext db,
        RentalConfirmationEmailService emailService,
        ILogger logger,
        CheckoutRentalPayload payload,
        string paymentIntentId,
        string sessionId)
    {
        foreach (var tenantPayload in payload.Tenants)
        {
            await CreateRentalForTenantAsync(db, emailService, logger, payload, tenantPayload, paymentIntentId, sessionId);
        }
    }

    private static async Task CreateRentalForTenantAsync(
        ApplicationDbContext db,
        RentalConfirmationEmailService emailService,
        ILogger logger,
        CheckoutRentalPayload payload,
        CheckoutTenantPayload tenantPayload,
        string paymentIntentId,
        string sessionId)
    {
        var tenantId = tenantPayload.TenantId;
        var idempotencyKey = string.IsNullOrWhiteSpace(payload.IdempotencyKey)
            ? $"{sessionId}:{tenantId}"
            : $"{payload.IdempotencyKey}:{tenantId}";

        var existing = await db.Rentals.AsNoTracking().FirstOrDefaultAsync(r =>
            r.TenantId == tenantId && r.IdempotencyKey == idempotencyKey);
        if (existing != null)
        {
            logger.LogInformation("Rental already exists for tenant {TenantId} session {SessionId}", tenantId, sessionId);
            return;
        }

        PaymentComputationResult computation;
        try
        {
            computation = await PaymentCalculator.ComputeAsync(
                tenantId,
                new PaymentQuoteRequest
                {
                    StartDateUtc = payload.StartDateUtc,
                    EndDateUtc = payload.EndDateUtc,
                    Items = tenantPayload.Items
                },
                db,
                CancellationToken.None,
                allowMixedTenants: false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to compute rental amount for tenant {TenantId} session {SessionId}", tenantId, sessionId);
            return;
        }

        var customer = await EnsureCustomerForTenantAsync(db, payload.Customer, tenantId);
        if (customer is null)
        {
            logger.LogWarning("Customer snapshot missing for tenant {TenantId} session {SessionId}", tenantId, sessionId);
            return;
        }

        var productIds = tenantPayload.Items.Select(i => i.ProductId).Distinct().ToList();
        var productMap = await db.Products
            .Where(p => p.TenantId == tenantId && productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id);

        if (productMap.Count != productIds.Count)
        {
            logger.LogWarning("One or more products missing for tenant {TenantId} session {SessionId}", tenantId, sessionId);
            return;
        }

        var rental = new Rental
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CustomerId = customer.Id,
            StartDateUtc = payload.StartDateUtc,
            EndDateUtc = payload.EndDateUtc,
            Notes = payload.Notes,
            IdempotencyKey = idempotencyKey,
            Status = RentalStatus.Confirmed,
            CreatedAtUtc = DateTime.UtcNow,
            TotalAmount = computation.TotalAmount,
            DepositAmount = computation.DepositAmount,
            PaymentIntentId = paymentIntentId,
            PaymentStatus = PaymentIntentStatus.Succeeded
        };

        foreach (var item in tenantPayload.Items)
        {
            if (!computation.ProductPrices.TryGetValue(item.ProductId, out var pricePerDay))
            {
                logger.LogWarning("Missing price for product {ProductId} in tenant {TenantId}", item.ProductId, tenantId);
                continue;
            }

            rental.Items.Add(new RentalItem
            {
                Id = Guid.NewGuid(),
                RentalId = rental.Id,
                ProductId = item.ProductId,
                Quantity = item.Quantity,
                PricePerDay = pricePerDay,
                Subtotal = pricePerDay * item.Quantity * computation.RentalDays
            });
        }

        db.Rentals.Add(rental);
        await db.SaveChangesAsync();

        await TrySendConfirmationEmailAsync(db, emailService, logger, customer, rental, productMap);

        logger.LogInformation("Rental {RentalId} created for tenant {TenantId} from checkout session {SessionId}", rental.Id, tenantId, sessionId);
    }

    private static async Task<DomainCustomer?> EnsureCustomerForTenantAsync(
        ApplicationDbContext db,
        CheckoutCustomerSnapshot snapshot,
        Guid tenantId)
    {
        DomainCustomer? customer = null;
        var normalizedEmail = snapshot.Email?.Trim().ToLowerInvariant();

        if (!string.IsNullOrWhiteSpace(normalizedEmail))
        {
            customer = await db.Customers.FirstOrDefaultAsync(c =>
                c.TenantId == tenantId &&
                c.Email != null &&
                c.Email.ToLower() == normalizedEmail);
        }

        if (customer is null && snapshot.CustomerId != Guid.Empty)
        {
            customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == snapshot.CustomerId && c.TenantId == tenantId);
        }

        if (customer is not null)
        {
            return customer;
        }

        var newCustomer = new DomainCustomer
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            FullName = string.IsNullOrWhiteSpace(snapshot.FullName)
                ? (snapshot.Email ?? "Klient")
                : snapshot.FullName,
            Email = string.IsNullOrWhiteSpace(snapshot.Email) ? null : snapshot.Email.Trim(),
            PhoneNumber = string.IsNullOrWhiteSpace(snapshot.PhoneNumber) ? null : snapshot.PhoneNumber.Trim(),
            Address = snapshot.Address,
            DocumentNumber = snapshot.DocumentNumber,
            Notes = snapshot.Notes,
            CreatedAtUtc = DateTime.UtcNow
        };

        await db.Customers.AddAsync(newCustomer);
        await db.SaveChangesAsync();
        return newCustomer;
    }

    private static async Task HandlePaymentStatusChangeAsync(
        Event stripeEvent,
        ApplicationDbContext db,
        string paymentStatus,
        RentalStatus fallbackRentalStatus,
        ILogger logger)
    {
        if (stripeEvent.Data.Object is not PaymentIntent paymentIntent)
        {
            logger.LogWarning("PaymentIntent event missing payload");
            return;
        }

        var metadata = paymentIntent.Metadata ?? new Dictionary<string, string>();
        string? key = null;
        metadata.TryGetValue(IdempotencyMetadataKey, out key);
        if (string.IsNullOrWhiteSpace(key))
        {
            metadata.TryGetValue($"custom_{IdempotencyMetadataKey}", out key);
        }

        var idempotencyKey = string.IsNullOrWhiteSpace(key)
            ? paymentIntent.Id
            : key;

        var rentals = await db.Rentals
            .Where(r => r.PaymentIntentId == paymentIntent.Id)
            .ToListAsync();

        if (rentals.Count == 0)
        {
            var idempotencyPrefix = $"{idempotencyKey}:";
            rentals = await db.Rentals
                .Where(r => r.IdempotencyKey != null && (r.IdempotencyKey == idempotencyKey || r.IdempotencyKey.StartsWith(idempotencyPrefix)))
                .ToListAsync();
        }

        if (rentals.Count == 0 && metadata.TryGetValue(TenantMetadataKey, out var tenantRaw) && Guid.TryParse(tenantRaw, out var tenantId))
        {
            rentals = await db.Rentals
                .Where(r => r.TenantId == tenantId)
                .ToListAsync();
        }

        if (rentals.Count == 0)
        {
            logger.LogInformation("PaymentIntent {PaymentIntentId} does not have rentals yet", paymentIntent.Id);
            return;
        }

        foreach (var rental in rentals)
        {
            rental.PaymentStatus = paymentStatus;

            if (paymentStatus == PaymentIntentStatus.Succeeded && rental.Status is RentalStatus.Pending or RentalStatus.Draft)
            {
                rental.Status = RentalStatus.Confirmed;
            }
            else if (paymentStatus == PaymentIntentStatus.Canceled || paymentStatus == PaymentIntentStatus.Failed)
            {
                rental.Status = fallbackRentalStatus;
            }
        }

        await db.SaveChangesAsync();
        logger.LogInformation("Updated {Count} rentals payment status -> {Status}", rentals.Count, paymentStatus);
    }

    private static async Task HandleRefundAsync(Event stripeEvent, ApplicationDbContext db, ILogger logger)
    {
        if (stripeEvent.Data.Object is not Charge charge || string.IsNullOrWhiteSpace(charge.PaymentIntentId))
        {
            logger.LogWarning("charge.refunded event missing charge/paymentIntent");
            return;
        }

        var rentals = await db.Rentals.Where(r => r.PaymentIntentId == charge.PaymentIntentId).ToListAsync();
        if (rentals.Count == 0)
        {
            logger.LogInformation("Refunded charge {ChargeId} has no matching rental", charge.Id);
            return;
        }

        foreach (var rental in rentals)
        {
            rental.PaymentStatus = PaymentIntentStatus.Canceled;
            rental.Status = RentalStatus.Cancelled;
        }

        await db.SaveChangesAsync();

        logger.LogInformation("Marked {Count} rentals as refunded for payment {PaymentIntentId}", rentals.Count, charge.PaymentIntentId);
    }

    private static CheckoutRentalPayload? DecodePayload(string encodedPayload, ILogger logger)
    {
        try
        {
            var jsonBytes = Convert.FromBase64String(encodedPayload);
            var payload = JsonSerializer.Deserialize<CheckoutRentalPayload>(jsonBytes, PayloadSerializerOptions);
            return payload;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to decode checkout payload metadata");
            return null;
        }
    }

    private static async Task TrySendConfirmationEmailAsync(
        ApplicationDbContext db,
        RentalConfirmationEmailService emailService,
        ILogger logger,
        DomainCustomer customer,
        Rental rental,
        IReadOnlyDictionary<Guid, DomainProduct> products)
    {
        try
        {
            var companyInfo = await db.CompanyInfos.FirstOrDefaultAsync(ci => ci.TenantId == rental.TenantId);
            var itemTuples = rental.Items
                .Select(item => (products[item.ProductId], item.Quantity))
                .ToList();

            // Generuj i zapisz PDF umowy (nawet jeśli klient nie ma emaila)
            var contractUrl = await emailService.GenerateAndSaveContractAsync(rental, customer, itemTuples, companyInfo);
            if (!string.IsNullOrWhiteSpace(contractUrl))
            {
                rental.ContractUrl = contractUrl;
                logger.LogInformation("Contract PDF saved for rental {RentalId}: {ContractUrl}", rental.Id, contractUrl);
            }

            // Wyślij email z potwierdzeniem (jeśli klient ma email)
            if (!string.IsNullOrWhiteSpace(customer.Email))
            {
                await emailService.SendRentalConfirmationAsync(
                    customer.Email,
                    customer.FullName ?? customer.Email,
                    customer,
                    rental,
                    itemTuples,
                    companyInfo);

                rental.IsEmailSent = true;
                logger.LogInformation("Confirmation email sent to {Email} for rental {RentalId}", customer.Email, rental.Id);
            }
            else
            {
                logger.LogWarning("Customer {CustomerId} has no email, skipping confirmation message", customer.Id);
            }

            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to process confirmation for rental {RentalId}", rental.Id);
        }
    }
}
