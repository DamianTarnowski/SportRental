using FluentAssertions;
using SportRental.Client.Services;
using SportRental.Shared.Models;

namespace SportRental.Client.Tests.Services;

public class CheckoutFinalizationRetrierTests
{
    [Fact]
    public async Task FinalizeAsync_RetriesTransientResponseAndReturnsSuccess()
    {
        var attempts = 0;

        var result = await CheckoutFinalizationRetrier.FinalizeAsync(
            _ => Task.FromResult<FinalizeSessionResponse?>(++attempts < 3
                ? new(false, "Jeszcze przetwarzamy", null, Retryable: true)
                : new(true, "Gotowe", Guid.NewGuid())),
            delayAsync: static (_, _) => Task.CompletedTask);

        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        attempts.Should().Be(3);
    }

    [Fact]
    public async Task FinalizeAsync_DoesNotRetryPermanentOrRefundedFailure()
    {
        var permanentAttempts = 0;
        var refundedAttempts = 0;

        var permanent = await CheckoutFinalizationRetrier.FinalizeAsync(
            _ =>
            {
                permanentAttempts++;
                return Task.FromResult<FinalizeSessionResponse?>(new(
                    false,
                    "Wymaga kontaktu z obsługą",
                    null));
            },
            delayAsync: static (_, _) => Task.CompletedTask);
        var refunded = await CheckoutFinalizationRetrier.FinalizeAsync(
            _ =>
            {
                refundedAttempts++;
                return Task.FromResult<FinalizeSessionResponse?>(new(
                    false,
                    "Kwota zwrócona",
                    null,
                    Refunded: true));
            },
            delayAsync: static (_, _) => Task.CompletedTask);

        permanent.Should().NotBeNull();
        permanent!.Retryable.Should().BeFalse();
        refunded.Should().NotBeNull();
        refunded!.Refunded.Should().BeTrue();
        permanentAttempts.Should().Be(1);
        refundedAttempts.Should().Be(1);
    }

    [Fact]
    public async Task FinalizeAsync_RetriesMissingResponsesUpToLimit()
    {
        var attempts = 0;

        var result = await CheckoutFinalizationRetrier.FinalizeAsync(
            _ =>
            {
                attempts++;
                return Task.FromResult<FinalizeSessionResponse?>(null);
            },
            maxAttempts: 3,
            delayAsync: static (_, _) => Task.CompletedTask);

        result.Should().BeNull();
        attempts.Should().Be(3);
    }
}
