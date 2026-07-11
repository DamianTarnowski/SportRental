using FluentAssertions;
using SportRental.Admin.Api;

namespace SportRental.Admin.Tests.Api;

public sealed class LegacyPublicTextNormalizerTests
{
    [Theory]
    [InlineData("ul. KrupĂłwki", "ul. Krupówki")]
    [InlineData("FloriaĹ„ska", "Floriańska")]
    [InlineData("MarszaĹ‚kowska", "Marszałkowska")]
    public void Normalize_RepairsWindows1250DecodedUtf8(string legacyValue, string expected)
    {
        LegacyPublicTextNormalizer.Normalize(legacyValue).Should().Be(expected);
    }

    [Fact]
    public void Normalize_LeavesCorrectPolishTextUnchanged()
    {
        const string value = "Zażółć gęślą jaźń, ul. Wiślana 7";

        LegacyPublicTextNormalizer.Normalize(value).Should().Be(value);
    }

    [Fact]
    public void Normalize_LeavesLegalForeignTextWithMarkerCharacterUnchanged()
    {
        const string value = "Älmhult, Sverige";

        LegacyPublicTextNormalizer.Normalize(value).Should().Be(value);
    }

    [Theory]
    [InlineData("Kraków", "kraków", "krakăłw")]
    [InlineData("KrakĂłw", "kraków", "krakăłw")]
    [InlineData("Łódź", "łódź", "ĺ‚ăłdĺş")]
    public void GetFilterCandidates_MatchesCanonicalAndLegacyRows(
        string input,
        string canonical,
        string legacy)
    {
        LegacyPublicTextNormalizer.GetFilterCandidates(input)
            .Should().Be((canonical, legacy));
    }
}
