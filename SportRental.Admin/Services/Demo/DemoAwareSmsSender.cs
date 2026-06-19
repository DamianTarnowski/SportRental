using SportRental.Admin.Services.Sms;

namespace SportRental.Admin.Services.Demo;

/// Decorator nad ISmsSender — w trybie demo NIE wysyła SMS-ów do realnych numerów,
/// tylko loguje "DEMO SANDBOX: SMS to ... message ...". Realny tryb passthrough.
public class DemoAwareSmsSender : ISmsSender
{
    private readonly ISmsSender _inner;
    private readonly IDemoGuard _guard;
    private readonly ILogger<DemoAwareSmsSender> _logger;

    public DemoAwareSmsSender(ISmsSender inner, IDemoGuard guard, ILogger<DemoAwareSmsSender> logger)
    {
        _inner = inner;
        _guard = guard;
        _logger = logger;
    }

    public async Task SendAsync(string phoneNumber, string message, CancellationToken ct = default)
    {
        if (await _guard.IsCurrentTenantDemoAsync(ct))
        {
            _logger.LogInformation("DEMO SANDBOX: SMS to {Phone}: {Message}", phoneNumber, message);
            return;
        }
        await _inner.SendAsync(phoneNumber, message, ct);
    }

    public async Task SendThanksMessageAsync(string phoneNumber, string customerName, string? customMessage = null, CancellationToken ct = default)
    {
        if (await _guard.IsCurrentTenantDemoAsync(ct))
        {
            _logger.LogInformation("DEMO SANDBOX: SMS thanks to {Phone} ({Name}): {Msg}", phoneNumber, customerName, customMessage);
            return;
        }
        await _inner.SendThanksMessageAsync(phoneNumber, customerName, customMessage, ct);
    }

    public async Task SendReminderAsync(string phoneNumber, string customerName, string? customMessage = null, CancellationToken ct = default)
    {
        if (await _guard.IsCurrentTenantDemoAsync(ct))
        {
            _logger.LogInformation("DEMO SANDBOX: SMS reminder to {Phone} ({Name}): {Msg}", phoneNumber, customerName, customMessage);
            return;
        }
        await _inner.SendReminderAsync(phoneNumber, customerName, customMessage, ct);
    }

    public async Task SendConfirmationRequestAsync(string phoneNumber, string customerName, Guid rentalId, CancellationToken ct = default)
    {
        if (await _guard.IsCurrentTenantDemoAsync(ct))
        {
            _logger.LogInformation("DEMO SANDBOX: SMS confirmation to {Phone} ({Name}) rental {RentalId}", phoneNumber, customerName, rentalId);
            return;
        }
        await _inner.SendConfirmationRequestAsync(phoneNumber, customerName, rentalId, ct);
    }

    public async Task SendContractConfirmationRequestAsync(string phoneNumber, string customerName, Guid rentalId, CancellationToken ct = default)
    {
        if (await _guard.IsCurrentTenantDemoAsync(ct))
        {
            _logger.LogInformation("DEMO SANDBOX: SMS contract confirm to {Phone} ({Name}) rental {RentalId}", phoneNumber, customerName, rentalId);
            return;
        }
        await _inner.SendContractConfirmationRequestAsync(phoneNumber, customerName, rentalId, ct);
    }

    public async Task SendContractConfirmationRequestAsync(string phoneNumber, string customerName, Guid rentalId, string? customerEmail, CancellationToken ct = default)
    {
        if (await _guard.IsCurrentTenantDemoAsync(ct))
        {
            _logger.LogInformation("DEMO SANDBOX: SMS contract confirm to {Phone} ({Name}) rental {RentalId} email {Email}", phoneNumber, customerName, rentalId, customerEmail);
            return;
        }
        await _inner.SendContractConfirmationRequestAsync(phoneNumber, customerName, rentalId, customerEmail, ct);
    }
}
