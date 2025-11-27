using Microsoft.Extensions.Configuration;
using SportRental.Api.Payments;
using Stripe;

namespace SportRental.Api.Tests;

internal static class StripeTestHelper
{
    private static readonly object LockObj = new();
    private static StripeOptions? _cachedOptions;

    public static StripeOptions GetStripeOptions()
    {
        if (_cachedOptions != null)
        {
            return _cachedOptions;
        }

        lock (LockObj)
        {
            if (_cachedOptions != null)
            {
                return _cachedOptions;
            }

            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.Test.json", optional: true)
                .AddEnvironmentVariables()
                .Build();

            var options = configuration.GetSection("Stripe").Get<StripeOptions>() ?? new StripeOptions();
            var secretFromEnv = Environment.GetEnvironmentVariable("STRIPE_SECRET_KEY");
            if (!string.IsNullOrWhiteSpace(secretFromEnv))
            {
                options.SecretKey = secretFromEnv;
            }

            if (string.IsNullOrWhiteSpace(options.SecretKey))
            {
                throw new InvalidOperationException("Stripe:SecretKey must be set for integration tests (appsettings.Test.json or STRIPE_SECRET_KEY env var).");
            }

            StripeConfiguration.ApiKey = options.SecretKey;
            _cachedOptions = options;
            return options;
        }
    }

    public static async Task ConfirmPaymentIntentAsync(string paymentIntentId)
    {
        if (string.IsNullOrWhiteSpace(paymentIntentId))
        {
            throw new ArgumentException("PaymentIntentId must be provided.", nameof(paymentIntentId));
        }

        // Ensure secret key initialized
        _ = GetStripeOptions();

        var service = new PaymentIntentService();
        await service.ConfirmAsync(paymentIntentId, new PaymentIntentConfirmOptions
        {
            PaymentMethod = "pm_card_visa"
        });
    }
}
