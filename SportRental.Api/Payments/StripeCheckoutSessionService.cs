using Stripe.Checkout;

namespace SportRental.Api.Payments;

internal sealed class StripeCheckoutSessionService : ICheckoutSessionService
{
    private readonly SessionService _sessionService = new();

    public Task<Session> CreateAsync(SessionCreateOptions options, CancellationToken cancellationToken)
    {
        return _sessionService.CreateAsync(options, null, cancellationToken);
    }

    public async Task<Session?> GetAsync(string sessionId, CancellationToken cancellationToken)
    {
        try
        {
            return await _sessionService.GetAsync(sessionId, null, null, cancellationToken);
        }
        catch
        {
            return null;
        }
    }
}
