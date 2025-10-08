using Microsoft.Extensions.Options;
using Stripe;
using Microsoft.EntityFrameworkCore;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;
using SportRental.Api.Services.Email;

namespace SportRental.Api.Payments;

public static class StripeWebhookEndpoints
{
    public static void MapStripeWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/webhooks/stripe", HandleStripeWebhook)
            .WithName("StripeWebhook")
            .WithTags("Webhooks")
            .WithSummary("Stripe webhook handler")
            .WithDescription("Obsługuje webhooks od Stripe (payment_intent.succeeded, payment_intent.payment_failed, etc.)")
            .ExcludeFromDescription(); // Don't show in Swagger (only Stripe calls this)
    }

    private static async Task<IResult> HandleStripeWebhook(
        HttpRequest request,
        IOptions<StripeOptions> stripeOptions,
        ApplicationDbContext db,
        RentalConfirmationEmailService emailService,
        ILogger<Program> logger)
    {
        var json = await new StreamReader(request.Body).ReadToEndAsync();
        
        try
        {
            var webhookSecret = stripeOptions.Value.WebhookSecret;
            
            if (string.IsNullOrWhiteSpace(webhookSecret))
            {
                logger.LogWarning("Stripe webhook secret not configured - accepting webhook without signature verification (DEV ONLY!)");
            }

            var signatureHeader = request.Headers["Stripe-Signature"].FirstOrDefault();
            
            Event? stripeEvent = null;
            
            if (!string.IsNullOrWhiteSpace(webhookSecret) && !string.IsNullOrWhiteSpace(signatureHeader))
            {
                // Production: verify webhook signature
                stripeEvent = EventUtility.ConstructEvent(json, signatureHeader, webhookSecret);
            }
            else
            {
                // Development: parse without verification
                stripeEvent = EventUtility.ParseEvent(json);
            }

            logger.LogInformation("Stripe webhook received: {EventType}", stripeEvent.Type);

            // Handle specific events
            switch (stripeEvent.Type)
            {
                case "payment_intent.succeeded":
                    await HandlePaymentSucceeded(stripeEvent, db, emailService, logger);
                    break;

                case "checkout.session.completed":
                    await HandleCheckoutSessionCompleted(stripeEvent, db, emailService, logger);
                    break;

                case "payment_intent.payment_failed":
                    await HandlePaymentFailed(stripeEvent, db, logger);
                    break;

                case "payment_intent.canceled":
                    await HandlePaymentCanceled(stripeEvent, db, logger);
                    break;

                case "charge.refunded":
                    await HandleChargeRefunded(stripeEvent, db, logger);
                    break;

                default:
                    logger.LogInformation("Unhandled Stripe event type: {EventType}", stripeEvent.Type);
                    break;
            }

            return Results.Ok(new { received = true });
        }
        catch (StripeException ex)
        {
            logger.LogError(ex, "Stripe webhook error: {Message}", ex.Message);
            return Results.BadRequest();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error processing Stripe webhook");
            return Results.StatusCode(500);
        }
    }

    private static async Task HandlePaymentSucceeded(Event stripeEvent, ApplicationDbContext db, RentalConfirmationEmailService emailService, ILogger logger)
    {
        var paymentIntent = stripeEvent.Data.Object as PaymentIntent;
        if (paymentIntent == null) return;

        logger.LogInformation("Payment succeeded: {PaymentIntentId}, Amount: {Amount} {Currency}",
            paymentIntent.Id, paymentIntent.Amount / 100m, paymentIntent.Currency);

        // Extract metadata - look for rental_id
        if (paymentIntent.Metadata.TryGetValue("rental_id", out var rentalIdStr) &&
            Guid.TryParse(rentalIdStr, out var rentalId))
        {
            // Find rental by ID from metadata
            var rental = await db.Rentals
                .Include(r => r.Items)
                .ThenInclude(i => i.Product)
                .Include(r => r.Customer)
                .FirstOrDefaultAsync(r => r.Id == rentalId);
            
            if (rental != null && rental.Customer != null)
            {
                // Mark as confirmed and paid
                rental.Status = RentalStatus.Confirmed;
                rental.PaymentStatus = "Succeeded";
                rental.IsEmailSent = false; // Will be set to true after email is sent
                await db.SaveChangesAsync();

                // Send confirmation email
                var items = rental.Items
                    .Where(i => i.Product != null)
                    .Select(i => (i.Product!, i.Quantity))
                    .ToList();

                if (items.Any() && !string.IsNullOrEmpty(rental.Customer.Email))
                {
                    // Get company info for PDF contract
                    var companyInfo = await db.CompanyInfos
                        .FirstOrDefaultAsync(c => c.TenantId == rental.TenantId);

                    await emailService.SendRentalConfirmationAsync(
                        rental.Customer.Email,
                        rental.Customer.FullName,
                        rental.Customer,
                        rental,
                        items,
                        companyInfo);

                    rental.IsEmailSent = true;
                    await db.SaveChangesAsync();
                }

                logger.LogInformation("Rental {RentalId} marked as confirmed and email sent", rental.Id);
            }
            else
            {
                logger.LogWarning("Rental {RentalId} not found or has no customer", rentalId);
            }
        }
        else if (paymentIntent.Metadata.TryGetValue("tenant_id", out var tenantIdStr) &&
            Guid.TryParse(tenantIdStr, out var tenantId))
        {
            // Fallback: Try to find by tenant and customer email from checkout session
            logger.LogInformation("No rental_id in metadata, payment for tenant {TenantId}", tenantId);
            
            // NOTE: For Checkout Session flow, the email will be sent from HandleCheckoutSessionCompleted
            // This handler is mainly for direct PaymentIntent API usage
        }
    }

    private static async Task HandleCheckoutSessionCompleted(Event stripeEvent, ApplicationDbContext db, RentalConfirmationEmailService emailService, ILogger logger)
    {
        var session = stripeEvent.Data.Object as Stripe.Checkout.Session;
        if (session == null) return;

        logger.LogInformation("Checkout session completed: {SessionId}, Customer: {Email}",
            session.Id, session.CustomerEmail);

        // Extract metadata from session
        if (session.Metadata.TryGetValue("tenant_id", out var tenantIdStr) &&
            Guid.TryParse(tenantIdStr, out var tenantId))
        {
            var startDateStr = session.Metadata["rental_start"];
            var endDateStr = session.Metadata["rental_end"];
            var customerEmail = session.CustomerEmail ?? session.Metadata.GetValueOrDefault("customer_email");

            if (string.IsNullOrEmpty(customerEmail))
            {
                logger.LogWarning("No customer email found in checkout session {SessionId}", session.Id);
                return;
            }

            logger.LogInformation("Checkout for tenant {TenantId}, dates {Start} - {End}",
                tenantId, startDateStr, endDateStr);

            // TODO: Create rental from checkout session metadata
            // For now, we're relying on the frontend to create rental with PaymentIntent ID
            // Then HandlePaymentSucceeded will send the email
            
            logger.LogInformation("Checkout session {SessionId} processed successfully", session.Id);
        }
    }

    private static Task HandlePaymentFailed(Event stripeEvent, ApplicationDbContext db, ILogger logger)
    {
        var paymentIntent = stripeEvent.Data.Object as PaymentIntent;
        if (paymentIntent == null) return Task.CompletedTask;

        logger.LogWarning("Payment failed: {PaymentIntentId}, Reason: {FailureMessage}",
            paymentIntent.Id, paymentIntent.LastPaymentError?.Message ?? "Unknown");

        // TODO: Mark rental as payment failed, notify customer
        
        return Task.CompletedTask;
    }

    private static Task HandlePaymentCanceled(Event stripeEvent, ApplicationDbContext db, ILogger logger)
    {
        var paymentIntent = stripeEvent.Data.Object as PaymentIntent;
        if (paymentIntent == null) return Task.CompletedTask;

        logger.LogInformation("Payment canceled: {PaymentIntentId}", paymentIntent.Id);

        // TODO: Update rental status to cancelled
        
        return Task.CompletedTask;
    }

    private static Task HandleChargeRefunded(Event stripeEvent, ApplicationDbContext db, ILogger logger)
    {
        var charge = stripeEvent.Data.Object as Charge;
        if (charge == null) return Task.CompletedTask;

        logger.LogInformation("Charge refunded: {ChargeId}, Amount: {Amount} {Currency}",
            charge.Id, charge.AmountRefunded / 100m, charge.Currency);

        // TODO: Update rental status to refunded, notify customer
        
        return Task.CompletedTask;
    }
}
