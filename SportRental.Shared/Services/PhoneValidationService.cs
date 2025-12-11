using PhoneNumbers;

namespace SportRental.Shared.Services;

/// <summary>
/// Serwis do walidacji i formatowania numerów telefonów z różnych krajów
/// </summary>
public static class PhoneValidationService
{
    private static readonly PhoneNumberUtil PhoneUtil = PhoneNumberUtil.GetInstance();

    /// <summary>
    /// Lista popularnych krajów z kodami kierunkowymi
    /// </summary>
    public static readonly List<CountryCode> Countries = new()
    {
        new("PL", "Polska", "+48", "🇵🇱"),
        new("DE", "Niemcy", "+49", "🇩🇪"),
        new("GB", "Wielka Brytania", "+44", "🇬🇧"),
        new("US", "USA", "+1", "🇺🇸"),
        new("FR", "Francja", "+33", "🇫🇷"),
        new("IT", "Włochy", "+39", "🇮🇹"),
        new("ES", "Hiszpania", "+34", "🇪🇸"),
        new("NL", "Holandia", "+31", "🇳🇱"),
        new("BE", "Belgia", "+32", "🇧🇪"),
        new("AT", "Austria", "+43", "🇦🇹"),
        new("CH", "Szwajcaria", "+41", "🇨🇭"),
        new("CZ", "Czechy", "+420", "🇨🇿"),
        new("SK", "Słowacja", "+421", "🇸🇰"),
        new("UA", "Ukraina", "+380", "🇺🇦"),
        new("SE", "Szwecja", "+46", "🇸🇪"),
        new("NO", "Norwegia", "+47", "🇳🇴"),
        new("DK", "Dania", "+45", "🇩🇰"),
        new("FI", "Finlandia", "+358", "🇫🇮"),
        new("PT", "Portugalia", "+351", "🇵🇹"),
        new("IE", "Irlandia", "+353", "🇮🇪"),
        new("LT", "Litwa", "+370", "🇱🇹"),
        new("LV", "Łotwa", "+371", "🇱🇻"),
        new("EE", "Estonia", "+372", "🇪🇪"),
        new("HU", "Węgry", "+36", "🇭🇺"),
        new("RO", "Rumunia", "+40", "🇷🇴"),
        new("BG", "Bułgaria", "+359", "🇧🇬"),
        new("HR", "Chorwacja", "+385", "🇭🇷"),
        new("SI", "Słowenia", "+386", "🇸🇮"),
        new("GR", "Grecja", "+30", "🇬🇷"),
    };

    /// <summary>
    /// Waliduje numer telefonu dla danego kraju
    /// </summary>
    public static PhoneValidationResult Validate(string? phoneNumber, string countryCode = "PL")
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return new PhoneValidationResult(false, "Numer telefonu jest wymagany", null, null);
        }

        try
        {
            var parsed = PhoneUtil.Parse(phoneNumber, countryCode);
            var isValid = PhoneUtil.IsValidNumber(parsed);

            if (!isValid)
            {
                var numberType = PhoneUtil.GetNumberType(parsed);
                return new PhoneValidationResult(
                    false, 
                    $"Nieprawidłowy numer telefonu dla kraju {countryCode}", 
                    null, 
                    null);
            }

            var formatted = PhoneUtil.Format(parsed, PhoneNumberFormat.E164);
            var national = PhoneUtil.Format(parsed, PhoneNumberFormat.NATIONAL);

            return new PhoneValidationResult(true, null, formatted, national);
        }
        catch (NumberParseException ex)
        {
            var message = ex.ErrorType switch
            {
                ErrorType.INVALID_COUNTRY_CODE => "Nieprawidłowy kod kraju",
                ErrorType.NOT_A_NUMBER => "To nie jest numer telefonu",
                ErrorType.TOO_SHORT_AFTER_IDD => "Numer jest za krótki",
                ErrorType.TOO_SHORT_NSN => "Numer jest za krótki",
                ErrorType.TOO_LONG => "Numer jest za długi",
                _ => "Nieprawidłowy format numeru telefonu"
            };
            return new PhoneValidationResult(false, message, null, null);
        }
    }

    /// <summary>
    /// Normalizuje numer telefonu do formatu E.164 (+48123456789)
    /// </summary>
    public static string? Normalize(string? phoneNumber, string countryCode = "PL")
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
            return null;

        var result = Validate(phoneNumber, countryCode);
        return result.IsValid ? result.E164Format : null;
    }

    /// <summary>
    /// Formatuje numer do czytelnej postaci krajowej
    /// </summary>
    public static string? FormatNational(string? phoneNumber, string countryCode = "PL")
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
            return null;

        var result = Validate(phoneNumber, countryCode);
        return result.IsValid ? result.NationalFormat : phoneNumber;
    }

    /// <summary>
    /// Próbuje wykryć kraj na podstawie numeru telefonu
    /// </summary>
    public static string? DetectCountry(string? phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
            return null;

        try
        {
            // Jeśli numer zaczyna się od +, spróbuj go sparsować
            if (phoneNumber.StartsWith("+"))
            {
                var parsed = PhoneUtil.Parse(phoneNumber, "ZZ");
                var regionCode = PhoneUtil.GetRegionCodeForNumber(parsed);
                return regionCode;
            }
        }
        catch
        {
            // Ignoruj błędy parsowania
        }

        return null;
    }

    /// <summary>
    /// Pobiera kod kraju na podstawie kodu ISO
    /// </summary>
    public static CountryCode? GetCountry(string isoCode)
    {
        return Countries.FirstOrDefault(c => c.IsoCode.Equals(isoCode, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Reprezentuje kraj z kodem kierunkowym
/// </summary>
public record CountryCode(string IsoCode, string Name, string DialCode, string Flag)
{
    public string DisplayName => $"{Flag} {Name} ({DialCode})";
    public string ShortDisplay => $"{Flag} {DialCode}";
}

/// <summary>
/// Wynik walidacji numeru telefonu
/// </summary>
public record PhoneValidationResult(
    bool IsValid,
    string? ErrorMessage,
    string? E164Format,
    string? NationalFormat);
