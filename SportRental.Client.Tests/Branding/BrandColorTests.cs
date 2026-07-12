using FluentAssertions;
using SportRental.Shared.Branding;

namespace SportRental.Client.Tests.Branding;

public sealed class BrandColorTests
{
    [Theory]
    [InlineData("#2f3c7e", "#2F3C7E")]
    [InlineData("  #F96167  ", "#F96167")]
    [InlineData("#000000", "#000000")]
    [InlineData("#FFFFFF", "#FFFFFF")]
    public void NormalizeHex_AcceptsOnlyFullSixDigitHexAndCanonicalizesIt(
        string input,
        string expected)
    {
        BrandColor.NormalizeHex(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#FFF")]
    [InlineData("#12345678")]
    [InlineData("#12GG56")]
    [InlineData("#123456;url(x)")]
    [InlineData("#123456; background:url(https://example.test/x)")]
    [InlineData("#123456);color:red")]
    public void NormalizeHex_RejectsValuesThatCouldEscapeCssColorContext(string input)
    {
        BrandColor.NormalizeHex(input).Should().BeNull();
    }

    [Fact]
    public void NormalizeOrDefault_UsesValidatedFallbackForUnsafeStoredValue()
    {
        var result = BrandColor.NormalizeOrDefault(
            "#123456; background:url(https://example.test/x)",
            BrandColor.DefaultPrimary);

        result.Should().Be(BrandColor.DefaultPrimary);
    }
}
