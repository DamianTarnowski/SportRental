using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using SportRental.Admin.Services.Contracts;

namespace SportRental.Admin.Tests.Services.Contracts;

public class ContractAccessLinkServiceTests
{
    private readonly ContractAccessLinkService _service =
        new(new EphemeralDataProtectionProvider());

    [Fact]
    public void CreatePath_ProducesOpaqueTokenThatResolvesToRental()
    {
        var rentalId = Guid.Parse("1954eb65-6f5c-4d0f-a7b0-84a4f8de3dc1");

        var path = _service.CreatePath(rentalId);
        var token = path[3..];

        path.Should().StartWith("/c/");
        token.ToLowerInvariant().Should().NotContain("1954eb65");
        token.Length.Should().BeGreaterThan(40);
        _service.TryResolveRentalId(token, out var resolved).Should().BeTrue();
        resolved.Should().Be(rentalId);
    }

    [Fact]
    public void TryResolveRentalId_RejectsTamperedToken()
    {
        var token = _service.CreatePath(Guid.NewGuid())[3..];
        var replacement = token[^1] == 'A' ? 'B' : 'A';
        var tampered = token[..^1] + replacement;

        _service.TryResolveRentalId(tampered, out var resolved).Should().BeFalse();
        resolved.Should().Be(Guid.Empty);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-valid-token")]
    public void TryResolveRentalId_RejectsInvalidInput(string token)
    {
        _service.TryResolveRentalId(token, out _).Should().BeFalse();
    }
}
