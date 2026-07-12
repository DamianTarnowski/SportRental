namespace SportRental.Shared.Branding;

/// <summary>
/// Wspólna walidacja kolorów brandingu używanych przez panel, API i klienta WASM.
/// Do stylów trafia wyłącznie zamknięty format #RRGGBB, nigdy surowa wartość z bazy.
/// </summary>
public static class BrandColor
{
    public const string DefaultPrimary = "#1B2350";
    public const string DefaultSecondary = "#2F3C7E";

    public static string? NormalizeHex(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        if (trimmed.Length != 7 || trimmed[0] != '#')
            return null;

        for (var index = 1; index < trimmed.Length; index++)
        {
            if (!IsAsciiHexDigit(trimmed[index]))
                return null;
        }

        return trimmed.ToUpperInvariant();
    }

    public static bool IsValidOrEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) || NormalizeHex(value) is not null;

    public static string NormalizeOrDefault(string? value, string fallback) =>
        NormalizeHex(value) ?? NormalizeHex(fallback) ?? DefaultPrimary;

    private static bool IsAsciiHexDigit(char value) =>
        value is >= '0' and <= '9' or
            >= 'A' and <= 'F' or
            >= 'a' and <= 'f';
}
