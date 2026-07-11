using System.Buffers;
using System.Text;

namespace SportRental.Admin.Api;

/// <summary>
/// Repairs legacy public location text that was stored after UTF-8 bytes had
/// accidentally been decoded as Windows-1250. The database remains untouched.
/// </summary>
internal static class LegacyPublicTextNormalizer
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private static readonly Encoding StrictWindows1250 = CreateStrictWindows1250();

    // UTF-8 lead bytes C3, C4 and C5 decoded as Windows-1250. These are the
    // characteristic prefixes visible in Polish mojibake, e.g. "Ăł" and "Ĺ‚".
    private static readonly SearchValues<char> MojibakeLeadMarkers = SearchValues.Create(
        ['\u0102', '\u00c4', '\u0139']);

    internal static string? Normalize(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        var markerCount = CountMarkers(value);
        if (markerCount == 0)
            return value;

        try
        {
            var candidate = StrictUtf8.GetString(StrictWindows1250.GetBytes(value));
            var candidateMarkerCount = CountMarkers(candidate);

            // Strict encoders already reject unmappable/invalid input. Keep the
            // explicit replacement check as a final guard if their setup changes.
            return !candidate.Contains('\uFFFD') && candidateMarkerCount < markerCount
                ? candidate
                : value;
        }
        catch (EncoderFallbackException)
        {
            return value;
        }
        catch (DecoderFallbackException)
        {
            return value;
        }
    }

    /// <summary>
    /// Produces the canonical filter value and the legacy Windows-1250-decoded
    /// UTF-8 representation still present in some rows. Callers must compare the
    /// two values explicitly because EF Core cannot translate this normalizer.
    /// </summary>
    internal static (string Canonical, string Legacy) GetFilterCandidates(string value)
    {
        var canonical = (Normalize(value.Trim()) ?? value.Trim()).ToLowerInvariant();
        try
        {
            var legacy = StrictWindows1250.GetString(StrictUtf8.GetBytes(canonical));
            return (canonical, legacy.ToLowerInvariant());
        }
        catch (EncoderFallbackException)
        {
            return (canonical, canonical);
        }
        catch (DecoderFallbackException)
        {
            return (canonical, canonical);
        }
    }

    private static int CountMarkers(string value)
    {
        var count = 0;
        foreach (var character in value)
        {
            if (MojibakeLeadMarkers.Contains(character))
                count++;
        }

        return count;
    }

    private static Encoding CreateStrictWindows1250()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(
            1250,
            EncoderFallback.ExceptionFallback,
            DecoderFallback.ExceptionFallback);
    }
}
