using SportRental.Shared.Models;

namespace SportRental.Client.Services;

public static class CheckoutFinalizationRetrier
{
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(3),
        TimeSpan.FromSeconds(5)
    ];

    public static async Task<FinalizeSessionResponse?> FinalizeAsync(
        Func<CancellationToken, Task<FinalizeSessionResponse?>> finalizeAttempt,
        int maxAttempts = 5,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(finalizeAttempt);
        if (maxAttempts < 1)
            throw new ArgumentOutOfRangeException(nameof(maxAttempts));

        delayAsync ??= static (delay, ct) => Task.Delay(delay, ct);
        FinalizeSessionResponse? lastResponse = null;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                lastResponse = await finalizeAttempt(cancellationToken);
                if (lastResponse is { Success: true } or { Refunded: true } ||
                    lastResponse is { Retryable: false })
                {
                    return lastResponse;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Brak odpowiedzi jest traktowany jak błąd przejściowy. Kolejna próba
                // jest bezpieczna, bo finalizacja backendu używa idempotency key.
                lastResponse = null;
            }

            if (attempt == maxAttempts - 1)
                break;

            var delay = RetryDelays[Math.Min(attempt, RetryDelays.Length - 1)];
            await delayAsync(delay, cancellationToken);
        }

        return lastResponse;
    }
}
