using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using SportRental.Admin.Services;
using Xunit;

namespace SportRental.Admin.Tests.Services;

/// <summary>
/// Pokrycie tokenu DataProtection dla publicznej ankiety /ankieta/{rentalId}?t={token}.
/// Krytyczne — token jest jedyną kontrolą dostępu (anonimowy endpoint).
/// </summary>
public class ReviewSurveyTokenServiceTests
{
    private readonly ReviewSurveyTokenService _service =
        new(new EphemeralDataProtectionProvider());

    [Fact]
    public void Generate_AndValidate_HappyPath_Returns_True()
    {
        var rentalId = Guid.NewGuid();
        var token = _service.Generate(rentalId);

        var ok = _service.TryValidate(token, rentalId, out var expiresAt);

        ok.Should().BeTrue();
        expiresAt.Should().BeAfter(DateTime.UtcNow);
        expiresAt.Should().BeBefore(DateTime.UtcNow.AddDays(61));
    }

    [Fact]
    public void Validate_TamperedToken_Fails()
    {
        var rentalId = Guid.NewGuid();
        var token = _service.Generate(rentalId);

        // Modyfikacja jednego znaku łamie HMAC w DataProtection.
        var tampered = token.Substring(0, token.Length - 1) +
                       (token[^1] == 'a' ? 'b' : 'a');

        var ok = _service.TryValidate(tampered, rentalId, out _);

        ok.Should().BeFalse();
    }

    [Fact]
    public void Validate_TokenForDifferentRental_Fails()
    {
        var rentalA = Guid.NewGuid();
        var rentalB = Guid.NewGuid();
        var token = _service.Generate(rentalA);

        var ok = _service.TryValidate(token, rentalB, out _);

        ok.Should().BeFalse();
    }

    [Fact]
    public void Validate_ExpiredToken_Fails()
    {
        var rentalId = Guid.NewGuid();
        var token = _service.Generate(rentalId, validFor: TimeSpan.FromMilliseconds(-1));

        var ok = _service.TryValidate(token, rentalId, out _);

        ok.Should().BeFalse();
    }

    [Fact]
    public void Validate_EmptyToken_Fails()
    {
        var ok = _service.TryValidate(string.Empty, Guid.NewGuid(), out _);
        ok.Should().BeFalse();
    }

    [Fact]
    public void Validate_GarbageToken_Fails()
    {
        var ok = _service.TryValidate("nie-jest-base64-data-protection", Guid.NewGuid(), out _);
        ok.Should().BeFalse();
    }

    [Fact]
    public void Validate_TokenFromOtherProvider_Fails()
    {
        // Token wygenerowany przez innego DataProtector (inny purpose) NIE powinien
        // dać się odszyfrować przez nasz purpose. To kluczowe — gdyby ktoś wziął token
        // z innego flow (np. confirmation) i wkleił do /ankieta?t=, powinien dostać 410.
        var rentalId = Guid.NewGuid();
        var otherProvider = new EphemeralDataProtectionProvider();
        var foreignProtector = otherProvider.CreateProtector("OtherPurpose");
        var foreignToken = foreignProtector.Protect($"{rentalId:N}|99999999999999");

        var ok = _service.TryValidate(foreignToken, rentalId, out _);

        ok.Should().BeFalse();
    }

    [Fact]
    public void Generate_ProducesDifferentTokensForSameRental()
    {
        // DataProtection embedduje IV w tokenie — kolejne wywołania Generate dla tego
        // samego rentalId muszą dawać różne stringi (deterministyczne tokeny =
        // ułatwianie precomputed attacku).
        var rentalId = Guid.NewGuid();
        var t1 = _service.Generate(rentalId);
        var t2 = _service.Generate(rentalId);

        t1.Should().NotBe(t2);
    }
}
