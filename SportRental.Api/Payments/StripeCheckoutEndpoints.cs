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
            return Results.BadRequest(new { error = ex.Message });
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
}
