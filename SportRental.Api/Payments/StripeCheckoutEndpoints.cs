using SportRental.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using SportRental.Shared.Models;

namespace SportRental.Api.Payments;

public static class StripeCheckoutEndpoints
{
    public static void MapStripeCheckoutEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/checkout").WithTags("Checkout");

        group.MapPost("/create-session", CreateCheckoutSession)
            .WithName("CreateCheckoutSession")
            .WithDescription("Tworzy Stripe Checkout Session (redirect do Stripe, bez JS!)")
            .Produces<CheckoutSessionResponse>(200);
    }

    private static async Task<IResult> CreateCheckoutSession(
        HttpRequest httpRequest,
        ApplicationDbContext db,
        IPaymentGateway gateway,
        CreateCheckoutSessionRequest request,
        IConfiguration configuration)
    {
        var tenantId = GetTenantId(httpRequest);
        
        // Validate tenant
        if (tenantId == Guid.Empty)
        {
            return Results.BadRequest(new { error = "Missing or invalid X-Tenant-Id header" });
        }

        // Validate dates
        if (request.StartDateUtc >= request.EndDateUtc)
        {
            return Results.BadRequest(new { error = "Start date must be before end date" });
        }

        // Validate items
        if (request.Items == null || !request.Items.Any())
        {
            return Results.BadRequest(new { error = "At least one item is required" });
        }
        
        try
        {
            // Compute rental pricing
            var total = 0m;
            var deposit = 0m;
            
            foreach (var item in request.Items)
            {
                var product = await db.Products.FirstOrDefaultAsync(p => p.Id == item.ProductId && p.TenantId == tenantId);
                if (product == null)
                {
                    return Results.BadRequest(new { error = $"Produkt {item.ProductId} nie znaleziony" });
                }

                var rentalDays = (request.EndDateUtc - request.StartDateUtc).Days;
                if (rentalDays <= 0) rentalDays = 1;

                total += product.DailyPrice * item.Quantity * rentalDays;
            }

            deposit = Math.Round(total * 0.3m, 2, MidpointRounding.AwayFromZero);

            var currency = "PLN";
            var metadata = new Dictionary<string, string>
            {
                ["tenant_id"] = tenantId.ToString(),
                ["rental_start"] = request.StartDateUtc.ToString("O"),
                ["rental_end"] = request.EndDateUtc.ToString("O"),
                ["deposit_amount"] = deposit.ToString("F2"),
                ["total_amount"] = total.ToString("F2"),
                ["customer_email"] = request.CustomerEmail,
                ["customer_id"] = request.CustomerId?.ToString() ?? ""
            };

            // Use IPaymentGateway instead of direct Stripe API
            var intent = await gateway.CreatePaymentIntentAsync(tenantId, total, deposit, currency, metadata);

            return Results.Ok(new CheckoutSessionResponse(
                SessionId: intent.Id.ToString(),
                Url: $"https://checkout.stripe.com/pay/{intent.Id}", // Stripe-compatible URL
                ExpiresAt: DateTime.UtcNow.AddDays(1)
            ));
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = $"Error: {ex.Message}" });
        }
    }

    private static Guid GetTenantId(HttpRequest request)
    {
        if (request.Headers.TryGetValue("X-Tenant-Id", out var values) && Guid.TryParse(values.FirstOrDefault(), out var tenantId))
            return tenantId;
        
        // Try from JWT claims
        if (request.HttpContext.User.Identity?.IsAuthenticated == true)
        {
            var tenantClaim = request.HttpContext.User.FindFirst("tenant-id");
            if (tenantClaim != null && Guid.TryParse(tenantClaim.Value, out var jwtTenantId))
                return jwtTenantId;
        }
        
        return Guid.Empty;
    }
}
