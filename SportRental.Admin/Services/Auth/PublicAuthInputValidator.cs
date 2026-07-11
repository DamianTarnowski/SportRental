using System.ComponentModel.DataAnnotations;

namespace SportRental.Admin.Services.Auth;

/// <summary>
/// Walidacja danych z anonimowych endpointów uwierzytelniania. Ograniczenia są
/// celowo wykonywane po stronie serwera — klient WASM nie jest granicą zaufania.
/// </summary>
public static class PublicAuthInputValidator
{
    public const int MaxEmailLength = 254;
    public const int MaxPasswordLength = 128;
    public const int MaxFullNameLength = 120;
    public const int MaxPhoneNumberLength = 32;
    public const int MaxAddressLength = 300;
    public const int MaxDocumentNumberLength = 64;
    public const int MaxNotesLength = 1_000;

    private static readonly EmailAddressAttribute EmailAddress = new();

    public static string? ValidateRegister(
        string? email,
        string? password,
        string? fullName,
        string? phoneNumber,
        string? documentNumber)
    {
        var emailError = ValidateEmail(email);
        if (emailError is not null)
            return emailError;

        if (string.IsNullOrEmpty(password))
            return "Hasło jest wymagane.";
        if (password.Length is < 12 or > MaxPasswordLength)
            return $"Hasło musi mieć od 12 do {MaxPasswordLength} znaków.";
        if (!password.Any(char.IsUpper) ||
            !password.Any(char.IsLower) ||
            !password.Any(char.IsDigit))
        {
            return "Hasło musi zawierać małą i wielką literę oraz cyfrę.";
        }

        var nameError = ValidateFullName(fullName);
        if (nameError is not null)
            return nameError;

        return ValidateOptionalFields(phoneNumber, address: null, documentNumber, notes: null);
    }

    public static string? ValidateGuestSession(
        string? email,
        string? fullName,
        string? phoneNumber,
        string? address,
        string? documentNumber,
        string? notes)
    {
        var emailError = ValidateEmail(email);
        if (emailError is not null)
            return emailError;

        var nameError = ValidateFullName(fullName);
        if (nameError is not null)
            return nameError;

        return ValidateOptionalFields(phoneNumber, address, documentNumber, notes);
    }

    public static bool TryNormalizeEmail(string? email, out string normalizedEmail)
    {
        normalizedEmail = email?.Trim().ToLowerInvariant() ?? string.Empty;
        return normalizedEmail.Length is > 0 and <= MaxEmailLength &&
               !normalizedEmail.Any(char.IsControl) &&
               EmailAddress.IsValid(normalizedEmail);
    }

    private static string? ValidateEmail(string? email) =>
        TryNormalizeEmail(email, out _)
            ? null
            : $"Podaj poprawny adres email (maksymalnie {MaxEmailLength} znaków).";

    private static string? ValidateFullName(string? fullName)
    {
        var trimmed = fullName?.Trim() ?? string.Empty;
        if (trimmed.Length is < 2 or > MaxFullNameLength || trimmed.Any(char.IsControl))
            return $"Imię i nazwisko musi mieć od 2 do {MaxFullNameLength} znaków.";

        return null;
    }

    private static string? ValidateOptionalFields(
        string? phoneNumber,
        string? address,
        string? documentNumber,
        string? notes)
    {
        if (!string.IsNullOrWhiteSpace(phoneNumber) && !IsValidPhoneNumber(phoneNumber))
            return "Podaj poprawny numer telefonu.";

        if (!IsValidOptionalText(address, MaxAddressLength, allowLineBreaks: true))
            return $"Adres może mieć maksymalnie {MaxAddressLength} znaków.";

        if (!IsValidOptionalText(documentNumber, MaxDocumentNumberLength, allowLineBreaks: false))
            return $"Numer dokumentu może mieć maksymalnie {MaxDocumentNumberLength} znaki.";

        if (!IsValidOptionalText(notes, MaxNotesLength, allowLineBreaks: true))
            return $"Uwagi mogą mieć maksymalnie {MaxNotesLength} znaków.";

        return null;
    }

    private static bool IsValidPhoneNumber(string phoneNumber)
    {
        var value = phoneNumber.Trim();
        if (value.Length is < 7 or > MaxPhoneNumberLength)
            return false;

        var digitCount = 0;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (char.IsDigit(character))
            {
                digitCount++;
                continue;
            }

            if (character == '+' && index == 0)
                continue;

            if (character is not (' ' or '-' or '(' or ')'))
                return false;
        }

        // E.164 dopuszcza maksymalnie 15 cyfr; formatowanie nie zwiększa limitu.
        return digitCount is >= 7 and <= 15;
    }

    private static bool IsValidOptionalText(string? value, int maxLength, bool allowLineBreaks)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
            return false;

        return !trimmed.Any(character =>
            char.IsControl(character) &&
            !(allowLineBreaks && character is ('\r' or '\n' or '\t')));
    }
}
