using System.Collections.Concurrent;
using SportRental.Shared.Models;

namespace SportRental.Api.Payments;

public class MockPaymentGateway : IPaymentGateway
{
    private readonly ConcurrentDictionary<string, PaymentIntentState> _intents = new();
    private readonly TimeSpan _defaultTtl = TimeSpan.FromMinutes(30);

    // IPaymentGateway implementation
    public Task<PaymentIntentDto> CreatePaymentIntentAsync(Guid tenantId, decimal amount, decimal depositAmount, string currency, Dictionary<string, string>? metadata = null)
    {
        return Task.FromResult(Create(tenantId, amount, depositAmount, currency));
    }

    public Task<PaymentIntentDto?> GetPaymentIntentAsync(Guid tenantId, string id)
    {
        return Task.FromResult(Get(tenantId, id));
    }

    public Task<bool> CapturePaymentAsync(Guid tenantId, string id)
    {
        return Task.FromResult(TryMarkAsCaptured(tenantId, id));
    }

    public Task<bool> CancelPaymentAsync(Guid tenantId, string id)
    {
        if (_intents.TryGetValue(id, out var state) && HasAccess(state.TenantId, tenantId))
        {
            state.Status = PaymentIntentStatus.Canceled;
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    public Task<bool> RefundPaymentAsync(Guid tenantId, string id, decimal? amount = null, string? reason = null)
    {
        // Mock refund - just mark as canceled for tests
        return CancelPaymentAsync(tenantId, id);
    }

    // Legacy synchronous methods for backward compatibility
    public PaymentIntentDto Create(Guid tenantId, decimal amount, decimal depositAmount, string currency)
    {
        var now = DateTime.UtcNow;
        var state = new PaymentIntentState
        {
            Id = $"pi_mock_{Guid.NewGuid():N}",
            TenantId = tenantId,
            Amount = amount,
            DepositAmount = depositAmount,
            Currency = currency,
            Status = PaymentIntentStatus.RequiresPaymentMethod, // Mock = requires user payment
            CreatedAtUtc = now,
            ExpiresAtUtc = now.Add(_defaultTtl)
        };

        _intents[state.Id] = state;
        return ToDto(state);
    }

    public PaymentIntentDto? Get(Guid tenantId, string id)
    {
        if (!_intents.TryGetValue(id, out var state))
        {
            return null;
        }

        if (!HasAccess(state.TenantId, tenantId))
        {
            return null;
        }

        if (state.ExpiresAtUtc < DateTime.UtcNow)
        {
            _intents.TryRemove(id, out _);
            return null;
        }

        return ToDto(state);
    }

    public bool TryMarkAsCaptured(Guid tenantId, string id)
    {
        if (!_intents.TryGetValue(id, out var state))
        {
            return false;
        }

        if (!HasAccess(state.TenantId, tenantId))
        {
            return false;
        }

        if (state.ExpiresAtUtc < DateTime.UtcNow)
        {
            _intents.TryRemove(id, out _);
            return false;
        }

        state.Status = PaymentIntentStatus.Succeeded;
        return true;
    }

    private static PaymentIntentDto ToDto(PaymentIntentState state)
    {
        return new PaymentIntentDto
        {
            Id = state.Id,
            Amount = state.Amount,
            DepositAmount = state.DepositAmount,
            Currency = state.Currency,
            Status = state.Status,
            CreatedAtUtc = state.CreatedAtUtc,
            ExpiresAtUtc = state.ExpiresAtUtc,
            ClientSecret = $"mock_secret_{state.Id}"
        };
    }

    private static bool HasAccess(Guid intentTenantId, Guid requestedTenantId)
    {
        return requestedTenantId == Guid.Empty || intentTenantId == Guid.Empty || intentTenantId == requestedTenantId;
    }

    private sealed class PaymentIntentState
    {
        public string Id { get; set; } = string.Empty;
        public Guid TenantId { get; set; }
        public decimal Amount { get; set; }
        public decimal DepositAmount { get; set; }
        public string Currency { get; set; } = "PLN";
        public string Status { get; set; } = PaymentIntentStatus.Pending;
        public DateTime CreatedAtUtc { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
    }
}
