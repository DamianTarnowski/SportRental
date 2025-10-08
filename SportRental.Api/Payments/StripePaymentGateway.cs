using Microsoft.Extensions.Options;
using Stripe;
using SportRental.Shared.Models;

namespace SportRental.Api.Payments;

public sealed class StripePaymentGateway : IPaymentGateway
{
    private readonly StripeOptions _options;
    private readonly PaymentIntentService _paymentIntentService;
    private readonly RefundService _refundService;

    public StripePaymentGateway(IOptions<StripeOptions> options)
    {
        _options = options.Value;
        
        if (string.IsNullOrWhiteSpace(_options.SecretKey))
        {
            throw new InvalidOperationException("Stripe:SecretKey is required in configuration");
        }

        StripeConfiguration.ApiKey = _options.SecretKey;
        
        _paymentIntentService = new PaymentIntentService();
        _refundService = new RefundService();
    }

    public async Task<PaymentIntentDto> CreatePaymentIntentAsync(
        Guid tenantId, 
        decimal amount, 
        decimal depositAmount, 
        string currency, 
        Dictionary<string, string>? metadata = null)
    {
        // Stripe uses smallest currency unit (grosze for PLN, cents for USD)
        var amountInCents = (long)(amount * 100);
        var depositInCents = (long)(depositAmount * 100);

        var createOptions = new PaymentIntentCreateOptions
        {
            Amount = amountInCents,
            Currency = currency.ToLowerInvariant(),
            AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions
            {
                Enabled = true,
                AllowRedirects = "never" // For rental equipment, we want immediate payments only
            },
            CaptureMethod = "automatic", // Auto-capture for deposits
            Metadata = new Dictionary<string, string>
            {
                ["tenant_id"] = tenantId.ToString(),
                ["deposit_amount"] = depositInCents.ToString(),
                ["total_amount"] = amountInCents.ToString(),
                ["source"] = "sport_rental_api"
            }
        };

        // Add custom metadata if provided
        if (metadata != null)
        {
            foreach (var (key, value) in metadata)
            {
                createOptions.Metadata[$"custom_{key}"] = value;
            }
        }

        var paymentIntent = await _paymentIntentService.CreateAsync(createOptions);

        return MapToDto(paymentIntent, depositAmount);
    }

    public async Task<PaymentIntentDto?> GetPaymentIntentAsync(Guid tenantId, Guid id)
    {
        try
        {
            var paymentIntent = await _paymentIntentService.GetAsync(id.ToString());
            
            // Verify tenant ownership
            if (paymentIntent.Metadata.TryGetValue("tenant_id", out var storedTenantId))
            {
                if (!Guid.TryParse(storedTenantId, out var parsedTenantId) || parsedTenantId != tenantId)
                {
                    return null;
                }
            }
            else
            {
                return null; // No tenant metadata = not our payment
            }

            var depositAmount = paymentIntent.Metadata.TryGetValue("deposit_amount", out var deposit)
                ? decimal.Parse(deposit) / 100m
                : 0m;

            return MapToDto(paymentIntent, depositAmount);
        }
        catch (StripeException)
        {
            return null;
        }
    }

    public async Task<bool> CapturePaymentAsync(Guid tenantId, Guid id)
    {
        try
        {
            var paymentIntent = await _paymentIntentService.GetAsync(id.ToString());
            
            // Verify tenant
            if (!VerifyTenantOwnership(paymentIntent, tenantId))
            {
                return false;
            }

            if (paymentIntent.Status == "requires_capture")
            {
                var captureOptions = new PaymentIntentCaptureOptions();
                await _paymentIntentService.CaptureAsync(id.ToString(), captureOptions);
            }

            return true;
        }
        catch (StripeException)
        {
            return false;
        }
    }

    public async Task<bool> CancelPaymentAsync(Guid tenantId, Guid id)
    {
        try
        {
            var paymentIntent = await _paymentIntentService.GetAsync(id.ToString());
            
            // Verify tenant
            if (!VerifyTenantOwnership(paymentIntent, tenantId))
            {
                return false;
            }

            if (paymentIntent.Status is "requires_payment_method" or "requires_confirmation" or "requires_action" or "requires_capture")
            {
                var cancelOptions = new PaymentIntentCancelOptions
                {
                    CancellationReason = "requested_by_customer"
                };
                await _paymentIntentService.CancelAsync(id.ToString(), cancelOptions);
                return true;
            }

            return false; // Already succeeded or canceled
        }
        catch (StripeException)
        {
            return false;
        }
    }

    public async Task<bool> RefundPaymentAsync(Guid tenantId, Guid id, decimal? amount = null, string? reason = null)
    {
        try
        {
            var paymentIntent = await _paymentIntentService.GetAsync(id.ToString());
            
            // Verify tenant
            if (!VerifyTenantOwnership(paymentIntent, tenantId))
            {
                return false;
            }

            if (paymentIntent.Status != "succeeded")
            {
                return false; // Can only refund succeeded payments
            }

            var refundOptions = new RefundCreateOptions
            {
                PaymentIntent = id.ToString(),
                Reason = reason switch
                {
                    "duplicate" => "duplicate",
                    "fraudulent" => "fraudulent",
                    _ => "requested_by_customer"
                }
            };

            if (amount.HasValue)
            {
                refundOptions.Amount = (long)(amount.Value * 100);
            }

            await _refundService.CreateAsync(refundOptions);
            return true;
        }
        catch (StripeException)
        {
            return false;
        }
    }

    private static PaymentIntentDto MapToDto(PaymentIntent paymentIntent, decimal depositAmount)
    {
        var status = paymentIntent.Status switch
        {
            "succeeded" => PaymentIntentStatus.Succeeded,
            "canceled" => PaymentIntentStatus.Canceled,
            "processing" => PaymentIntentStatus.Processing,
            "requires_payment_method" => PaymentIntentStatus.RequiresPaymentMethod,
            "requires_confirmation" => PaymentIntentStatus.RequiresConfirmation,
            "requires_action" => PaymentIntentStatus.RequiresAction,
            "requires_capture" => PaymentIntentStatus.RequiresCapture,
            _ => PaymentIntentStatus.Pending
        };

        // Stripe uses string IDs like "pi_3ABC...", we need to store it differently
        // For compatibility with existing GUID-based system, we'll hash the Stripe ID to a deterministic GUID
        var id = GenerateGuidFromStripeId(paymentIntent.Id);

        return new PaymentIntentDto
        {
            Id = id,
            Amount = paymentIntent.Amount / 100m,
            DepositAmount = depositAmount,
            Currency = paymentIntent.Currency.ToUpperInvariant(),
            Status = status,
            CreatedAtUtc = paymentIntent.Created,
            ExpiresAtUtc = paymentIntent.Created.AddHours(24), // Stripe payment intents expire after 24h
            ClientSecret = paymentIntent.ClientSecret // Needed for frontend Stripe.js
        };
    }
    
    /// <summary>
    /// Generates a deterministic GUID from Stripe ID for compatibility with existing system
    /// </summary>
    private static Guid GenerateGuidFromStripeId(string stripeId)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(stripeId));
        var guidBytes = new byte[16];
        Array.Copy(hashBytes, guidBytes, 16);
        return new Guid(guidBytes);
    }

    private static bool VerifyTenantOwnership(PaymentIntent paymentIntent, Guid tenantId)
    {
        if (paymentIntent.Metadata.TryGetValue("tenant_id", out var storedTenantId))
        {
            return Guid.TryParse(storedTenantId, out var parsedTenantId) && parsedTenantId == tenantId;
        }
        return false;
    }
}
