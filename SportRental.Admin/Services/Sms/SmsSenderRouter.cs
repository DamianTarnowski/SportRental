using Microsoft.Extensions.Options;

namespace SportRental.Admin.Services.Sms;

public class SmsSenderRouter : ISmsSender
{
    private readonly SmsRoutingSettings _routing;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SmsSenderRouter> _logger;

    public SmsSenderRouter(
        IOptions<SmsRoutingSettings> routing,
        IServiceProvider serviceProvider,
        ILogger<SmsSenderRouter> logger)
    {
        _routing = routing.Value;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    private ISmsSender ResolveInnerSender()
    {
        var provider = _routing.Provider?.Trim();

        if (string.IsNullOrWhiteSpace(provider))
        {
            _logger.LogWarning("Sms:Provider not configured. Falling back to SerwerSmsSender.");
            return _serviceProvider.GetRequiredService<SerwerSmsSender>();
        }

        return provider.ToLowerInvariant() switch
        {
            "smsapi" or "smsapi.pl" => _serviceProvider.GetRequiredService<SmsApiSender>(),
            "serwersms" or "serwersms.pl" or "serwersmsender" or "serwersms-sender" or "serwer" => _serviceProvider.GetRequiredService<SerwerSmsSender>(),
            "console" or "consolesms" => _serviceProvider.GetRequiredService<ConsoleSmsSender>(),
            _ => ResolveUnknownProvider(provider)
        };
    }

    private ISmsSender ResolveUnknownProvider(string provider)
    {
        _logger.LogWarning("Unknown Sms:Provider '{Provider}'. Falling back to SerwerSmsSender.", provider);
        return _serviceProvider.GetRequiredService<SerwerSmsSender>();
    }

    public Task SendAsync(string phoneNumber, string message, CancellationToken ct = default)
        => ResolveInnerSender().SendAsync(phoneNumber, message, ct);

    public Task SendThanksMessageAsync(string phoneNumber, string customerName, string? customMessage = null, CancellationToken ct = default)
        => ResolveInnerSender().SendThanksMessageAsync(phoneNumber, customerName, customMessage, ct);

    public Task SendReminderAsync(string phoneNumber, string customerName, string? customMessage = null, CancellationToken ct = default)
        => ResolveInnerSender().SendReminderAsync(phoneNumber, customerName, customMessage, ct);

    public Task SendConfirmationRequestAsync(string phoneNumber, string customerName, Guid rentalId, CancellationToken ct = default)
        => ResolveInnerSender().SendConfirmationRequestAsync(phoneNumber, customerName, rentalId, ct);

    public Task SendContractConfirmationRequestAsync(string phoneNumber, string customerName, Guid rentalId, CancellationToken ct = default)
        => ResolveInnerSender().SendContractConfirmationRequestAsync(phoneNumber, customerName, rentalId, ct);

    public Task SendContractConfirmationRequestAsync(string phoneNumber, string customerName, Guid rentalId, string? customerEmail, CancellationToken ct = default)
        => ResolveInnerSender().SendContractConfirmationRequestAsync(phoneNumber, customerName, rentalId, customerEmail, ct);
}
