using Stripe.Checkout;

namespace SportRental.Api.Payments;

public interface ICheckoutSessionService
{
    Task<Session> CreateAsync(SessionCreateOptions options, CancellationToken cancellationToken);
}
