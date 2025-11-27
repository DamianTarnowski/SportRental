using Stripe.Checkout;

namespace SportRental.Api.Payments;

internal sealed class StripeCheckoutSessionService : ICheckoutSessionService
{
    private readonly SessionService _sessionService = new();

    public Task<Session> CreateAsync(SessionCreateOptions options, CancellationToken cancellationToken)
    {
        return _sessionService.CreateAsync(options, null, cancellationToken);
    }
}
