using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SportRental.Infrastructure.Data;
using SportRental.Shared.Models;
using Stripe;
using Stripe.Checkout;

namespace SportRental.Api.Payments;

public static class StripeCheckoutEndpoints
{
    private const string CheckoutPayloadMetadataKey = "checkout_payload";
    private static readonly JsonSerializerOptions PayloadSerializerOptions = new(JsonSerializerDefaults.Web);

    public static void MapStripeCheckoutEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/checkout").WithTags("Checkout");

        group.MapPost("/create-session", CreateCheckoutSession)
            .WithName("CreateCheckoutSession")
            .WithDescription("Tworzy Stripe Checkout Session (redirect do Stripe, bez JS!)")
            .Produces<CheckoutSessionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .AllowAnonymous();

        group.MapPost("/finalize-session/{sessionId}", FinalizeCheckoutSession)
            .WithName("FinalizeCheckoutSession")
            .WithDescription("Finalizuje sesję checkout - tworzy wypożyczenie jeśli płatność się powiodła (fallback gdy webhook nie działa)")
            .Produces<FinalizeSessionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .AllowAnonymous();
    }

    private static async Task<IResult> CreateCheckoutSession(
        HttpRequest httpRequest,
        ApplicationDbContext db,
        CreateCheckoutSessionRequest request,
        IConfiguration configuration,
        IOptions<StripeOptions> stripeOptions,
        ICheckoutSessionService checkoutSessions)
    {
        if (request.StartDateUtc >= request.EndDateUtc)
        {
            return Results.BadRequest(new { error = "Start date must be before end date." });
        }

        if (request.Items == null || request.Items.Count == 0)
        {
            return Results.BadRequest(new { error = "At least one item is required." });
        }

        if (!request.CustomerId.HasValue)
        {
            return Results.BadRequest(new { error = "CustomerId is required for checkout." });
        }

        try
        {
            var computation = await PaymentCalculator.ComputeAsync(
                Guid.Empty,
                new PaymentQuoteRequest
                {
                    StartDateUtc = request.StartDateUtc,
                    EndDateUtc = request.EndDateUtc,
                    Items = request.Items
                        .Select(i => new CreateRentalItem { ProductId = i.ProductId, Quantity = i.Quantity })
                        .ToList()
                },
                db,
                httpRequest.HttpContext.RequestAborted);

            var customer = await db.Customers
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == request.CustomerId.Value, httpRequest.HttpContext.RequestAborted);

            if (customer is null)
            {
                return Results.BadRequest(new { error = "Customer not found." });
            }

            var stripe = stripeOptions.Value;
            if (string.IsNullOrWhiteSpace(stripe.SecretKey))
            {
                throw new InvalidOperationException("Stripe:SecretKey is not configured.");
            }

            StripeConfiguration.ApiKey = stripe.SecretKey;

            var successUrl = BuildReturnUrl(stripe.SuccessUrl ?? configuration["Stripe:SuccessUrl"], "https://localhost:5014/checkout/success");
            var cancelUrl = BuildReturnUrl(stripe.CancelUrl ?? configuration["Stripe:CancelUrl"], "https://localhost:5014/checkout/cancel");

            var idempotencyKey = $"checkout:{Guid.NewGuid():N}";
            var depositAmount = computation.DepositAmount <= 0 ? computation.TotalAmount : computation.DepositAmount;
            var depositUnitAmount = Math.Max(1, (long)Math.Round(depositAmount * 100, MidpointRounding.AwayFromZero));

            var checkoutPayload = new CheckoutRentalPayload
            {
                Customer = new CheckoutCustomerSnapshot
                {
                    CustomerId = customer.Id,
                    FullName = customer.FullName,
                    Email = customer.Email,
                    PhoneNumber = customer.PhoneNumber,
                    Address = customer.Address,
                    DocumentNumber = customer.DocumentNumber,
                    Notes = customer.Notes
                },
                StartDateUtc = request.StartDateUtc,
                EndDateUtc = request.EndDateUtc,
                Tenants = computation.Tenants
                    .Select(t => new CheckoutTenantPayload
                    {
                        TenantId = t.TenantId,
                        Items = t.Items.ToList(),
                        TotalAmount = t.TotalAmount,
                        DepositAmount = t.DepositAmount
                    })
                    .ToList(),
                Notes = null,
                IdempotencyKey = idempotencyKey,
                TotalAmount = computation.TotalAmount,
                DepositAmount = depositAmount
            };

            if (checkoutPayload.Tenants.Count == 0)
            {
                return Results.BadRequest(new { error = "Brak pozycji do finalizacji." });
            }

            var tenantIds = checkoutPayload.Tenants.Select(t => t.TenantId).Distinct().ToList();
            var metadata = BuildMetadata(tenantIds, request, computation, checkoutPayload, depositAmount);
            var customerEmail = string.IsNullOrWhiteSpace(request.CustomerEmail)
                ? checkoutPayload.Customer.Email
                : request.CustomerEmail;

            var sessionOptions = new SessionCreateOptions
            {
                SuccessUrl = AppendSessionPlaceholder(successUrl),
                CancelUrl = cancelUrl,
                Mode = "payment",
                CustomerEmail = customerEmail,
                ExpiresAt = DateTime.UtcNow.AddHours(23),
                PaymentIntentData = new SessionPaymentIntentDataOptions
                {
                    Metadata = metadata,
                    CaptureMethod = "automatic"
                },
                Metadata = metadata,
                ClientReferenceId = request.CustomerId.Value.ToString(),
                LineItems = new List<SessionLineItemOptions>
                {
                    new()
                    {
                        Quantity = 1,
                        PriceData = new SessionLineItemPriceDataOptions
                        {
                            Currency = (stripe.Currency ?? "pln").ToLowerInvariant(),
                            UnitAmount = depositUnitAmount,
                            ProductData = new SessionLineItemPriceDataProductDataOptions
                            {
                                Name = $"Depozyt za wypożyczenie ({request.StartDateUtc:dd.MM} - {request.EndDateUtc:dd.MM})",
                                Description = "Depozyt za wynajem sprzętu sportowego"
                            }
                        }
                    }
                }
            };

            var session = await checkoutSessions.CreateAsync(sessionOptions, httpRequest.HttpContext.RequestAborted);

            return Results.Ok(new CheckoutSessionResponse(
                SessionId: session.Id,
                Url: session.Url,
                ExpiresAt: session.ExpiresAt));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[CreateCheckoutSession] Error: {ex}");
            return Results.BadRequest(new { error = ex.Message, details = ex.ToString() });
        }
    }

    private static Dictionary<string, string> BuildMetadata(
        IReadOnlyCollection<Guid> tenantIds,
        CreateCheckoutSessionRequest request,
        PaymentComputationResult computation,
        CheckoutRentalPayload payload,
        decimal depositAmount)
    {
        var primaryTenant = tenantIds.Count == 1 ? tenantIds.First() : Guid.Empty;
        var metadata = new Dictionary<string, string>
        {
            ["tenant_id"] = primaryTenant.ToString(),
            ["tenant_ids"] = string.Join(",", tenantIds),
            ["customer_id"] = payload.Customer.CustomerId.ToString(),
            ["rental_start"] = request.StartDateUtc.ToString("O"),
            ["rental_end"] = request.EndDateUtc.ToString("O"),
            ["total_amount"] = payload.TotalAmount.ToString("F2", CultureInfo.InvariantCulture),
            ["deposit_amount"] = depositAmount.ToString("F2", CultureInfo.InvariantCulture),
            ["currency"] = "PLN",
            ["idempotency_key"] = payload.IdempotencyKey,
            ["rental_days"] = computation.RentalDays.ToString()
        };

        var encodedPayload = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(payload, PayloadSerializerOptions));
        if (encodedPayload.Length <= 450)
        {
            metadata[CheckoutPayloadMetadataKey] = encodedPayload;
        }
        else
        {
            var chunkSize = 450;
            var chunks = (int)Math.Ceiling(encodedPayload.Length / (double)chunkSize);
            metadata[$"{CheckoutPayloadMetadataKey}_parts"] = chunks.ToString(CultureInfo.InvariantCulture);
            for (var i = 0; i < chunks; i++)
            {
                var start = i * chunkSize;
                var len = Math.Min(chunkSize, encodedPayload.Length - start);
                metadata[$"{CheckoutPayloadMetadataKey}_{i}"] = encodedPayload.Substring(start, len);
            }
        }

        if (!string.IsNullOrWhiteSpace(payload.Customer.Email))
        {
            metadata["customer_email"] = payload.Customer.Email!;
        }

        return metadata;
    }

    private static string AppendSessionPlaceholder(string url)
    {
        return url.Contains("{CHECKOUT_SESSION_ID}", StringComparison.OrdinalIgnoreCase)
            ? url
            : (url.Contains('?')
                ? $"{url}&session_id={{CHECKOUT_SESSION_ID}}"
                : $"{url}?session_id={{CHECKOUT_SESSION_ID}}");
    }

    private static string BuildReturnUrl(string? configured, string fallback)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            return fallback;
        }
        return configured;
    }

    /// <summary>
    /// Finalizuje sesję checkout - sprawdza status płatności w Stripe i tworzy wypożyczenie.
    /// To jest fallback gdy webhook nie działa (np. lokalnie).
    /// </summary>
    private static async Task<IResult> FinalizeCheckoutSession(
        string sessionId,
        HttpRequest httpRequest,
        ApplicationDbContext db,
        IOptions<StripeOptions> stripeOptions,
        ICheckoutSessionService checkoutSessions,
        ILogger<Program> logger)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return Results.BadRequest(new { error = "Session ID is required." });
        }

        try
        {
            var stripe = stripeOptions.Value;
            if (string.IsNullOrWhiteSpace(stripe.SecretKey))
            {
                return Results.BadRequest(new { error = "Stripe not configured." });
            }

            StripeConfiguration.ApiKey = stripe.SecretKey;

            // Pobierz sesję z Stripe
            var session = await checkoutSessions.GetAsync(sessionId, cancellationToken: httpRequest.HttpContext.RequestAborted);
            
            if (session == null)
            {
                return Results.NotFound(new { error = "Session not found in Stripe." });
            }

            logger.LogInformation("Finalize session {SessionId}: Status={Status}, PaymentStatus={PaymentStatus}", 
                sessionId, session.Status, session.PaymentStatus);

            // Sprawdź czy płatność się powiodła
            if (session.PaymentStatus != "paid")
            {
                return Results.Ok(new FinalizeSessionResponse(
                    Success: false,
                    Message: $"Payment not completed. Status: {session.PaymentStatus}",
                    RentalId: null
                ));
            }

            // Sprawdź czy rental już istnieje (idempotency)
            var metadata = session.Metadata ?? new Dictionary<string, string>();
            string? idempotencyKey = null;
            metadata.TryGetValue("idempotency_key", out idempotencyKey);

            if (!string.IsNullOrWhiteSpace(idempotencyKey))
            {
                var existingRental = await db.Rentals
                    .AsNoTracking()
                    .FirstOrDefaultAsync(r => r.IdempotencyKey != null && r.IdempotencyKey.StartsWith(idempotencyKey));
                
                if (existingRental != null)
                {
                    logger.LogInformation("Rental already exists for session {SessionId}: {RentalId}", sessionId, existingRental.Id);
                    return Results.Ok(new FinalizeSessionResponse(
                        Success: true,
                        Message: "Rental already exists.",
                        RentalId: existingRental.Id
                    ));
                }
            }

            // Dekoduj payload z metadata
            var encodedPayload = TryReadPayloadMetadata(metadata, logger, sessionId);
            if (encodedPayload is null)
            {
                return Results.BadRequest(new { error = "Missing checkout payload in session metadata." });
            }

            var payload = DecodePayload(encodedPayload, logger);
            if (payload is null)
            {
                return Results.BadRequest(new { error = "Failed to decode checkout payload." });
            }

            payload = payload with { Tenants = payload.Tenants ?? new List<CheckoutTenantPayload>() };

            if (payload.Tenants.Count == 0)
            {
                return Results.BadRequest(new { error = "No tenant information in payload." });
            }

            // Utwórz wypożyczenia
            var paymentIntentId = session.PaymentIntentId ?? session.Id;
            var createdRentalIds = new List<Guid>();

            foreach (var tenantPayload in payload.Tenants)
            {
                var rentalId = await CreateRentalForTenantAsync(db, logger, payload, tenantPayload, paymentIntentId, sessionId);
                if (rentalId.HasValue)
                {
                    createdRentalIds.Add(rentalId.Value);
                }
            }

            if (createdRentalIds.Count == 0)
            {
                return Results.BadRequest(new { error = "Failed to create any rentals." });
            }

            logger.LogInformation("Created {Count} rental(s) for session {SessionId}", createdRentalIds.Count, sessionId);

            return Results.Ok(new FinalizeSessionResponse(
                Success: true,
                Message: $"Created {createdRentalIds.Count} rental(s).",
                RentalId: createdRentalIds.First()
            ));
        }
        catch (StripeException ex)
        {
            logger.LogError(ex, "Stripe error finalizing session {SessionId}", sessionId);
            return Results.BadRequest(new { error = $"Stripe error: {ex.Message}" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error finalizing session {SessionId}", sessionId);
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static string? TryReadPayloadMetadata(IDictionary<string, string> metadata, ILogger logger, string sessionId)
    {
        if (metadata.TryGetValue(CheckoutPayloadMetadataKey, out var encodedPayload))
        {
            return encodedPayload;
        }

        if (metadata.TryGetValue($"{CheckoutPayloadMetadataKey}_parts", out var partsRaw) &&
            int.TryParse(partsRaw, out var parts) && parts > 0)
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

            logger.LogWarning("Session {SessionId} missing some payload chunks", sessionId);
        }

        return null;
    }

    private static CheckoutRentalPayload? DecodePayload(string encodedPayload, ILogger logger)
    {
        try
        {
            var jsonBytes = Convert.FromBase64String(encodedPayload);
            return JsonSerializer.Deserialize<CheckoutRentalPayload>(jsonBytes, PayloadSerializerOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to decode checkout payload");
            return null;
        }
    }

    private static async Task<Guid?> CreateRentalForTenantAsync(
        ApplicationDbContext db,
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

        // Sprawdź czy już istnieje
        var existing = await db.Rentals.AsNoTracking()
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.IdempotencyKey == idempotencyKey);
        if (existing != null)
        {
            logger.LogInformation("Rental already exists for tenant {TenantId}", tenantId);
            return existing.Id;
        }

        // Oblicz kwoty
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
            logger.LogError(ex, "Failed to compute rental for tenant {TenantId}", tenantId);
            return null;
        }

        // Znajdź lub utwórz klienta
        var customer = await EnsureCustomerForTenantAsync(db, payload.Customer, tenantId);
        if (customer is null)
        {
            logger.LogWarning("Customer not found for tenant {TenantId}", tenantId);
            return null;
        }

        // Utwórz rental
        var rental = new SportRental.Infrastructure.Domain.Rental
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CustomerId = customer.Id,
            StartDateUtc = payload.StartDateUtc,
            EndDateUtc = payload.EndDateUtc,
            Notes = payload.Notes,
            IdempotencyKey = idempotencyKey,
            Status = SportRental.Infrastructure.Domain.RentalStatus.Confirmed,
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
                continue;
            }

            rental.Items.Add(new SportRental.Infrastructure.Domain.RentalItem
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

        logger.LogInformation("Created rental {RentalId} for tenant {TenantId}", rental.Id, tenantId);
        return rental.Id;
    }

    private static async Task<SportRental.Infrastructure.Domain.Customer?> EnsureCustomerForTenantAsync(
        ApplicationDbContext db,
        CheckoutCustomerSnapshot snapshot,
        Guid tenantId)
    {
        var normalizedEmail = snapshot.Email?.Trim().ToLowerInvariant();

        // Szukaj po emailu
        if (!string.IsNullOrWhiteSpace(normalizedEmail))
        {
            var customer = await db.Customers.FirstOrDefaultAsync(c =>
                c.TenantId == tenantId &&
                c.Email != null &&
                c.Email.ToLower() == normalizedEmail);
            if (customer != null) return customer;
        }

        // Szukaj po ID
        if (snapshot.CustomerId != Guid.Empty)
        {
            var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == snapshot.CustomerId);
            if (customer != null) return customer;
        }

        // Utwórz nowego
        var newCustomer = new SportRental.Infrastructure.Domain.Customer
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            FullName = string.IsNullOrWhiteSpace(snapshot.FullName) ? (snapshot.Email ?? "Klient") : snapshot.FullName,
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
}

public record FinalizeSessionResponse(bool Success, string Message, Guid? RentalId);
