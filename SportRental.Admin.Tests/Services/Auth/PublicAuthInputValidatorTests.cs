using SportRental.Admin.Services.Auth;

namespace SportRental.Admin.Tests.Services.Auth;

public sealed class PublicAuthInputValidatorTests
{
    [Fact]
    public void ValidateRegister_AcceptsNormalizedValidInput()
    {
        var error = PublicAuthInputValidator.ValidateRegister(
            "  Klient+test@Example.TEST ",
            "BezpieczneHaslo123",
            "Jan Kowalski",
            "+48 123-456-789",
            "ABC123456");

        Assert.Null(error);
        Assert.True(PublicAuthInputValidator.TryNormalizeEmail(
            "  Klient+test@Example.TEST ", out var normalized));
        Assert.Equal("klient+test@example.test", normalized);
    }

    [Theory]
    [InlineData("nie-email", "BezpieczneHaslo123", "Jan Kowalski")]
    [InlineData("klient@example.test", "za-krotkie", "Jan Kowalski")]
    [InlineData("klient@example.test", "same-male-litery-123", "Jan Kowalski")]
    [InlineData("klient@example.test", "BezpieczneHaslo123", "X")]
    public void ValidateRegister_RejectsInvalidIdentityFields(
        string email,
        string password,
        string fullName)
    {
        var error = PublicAuthInputValidator.ValidateRegister(
            email,
            password,
            fullName,
            phoneNumber: null,
            documentNumber: null);

        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("+48 CALL-NOW")]
    [InlineData("+4812345678901234")]
    public void ValidateGuestSession_RejectsInvalidPhoneFormat(string phoneNumber)
    {
        var error = PublicAuthInputValidator.ValidateGuestSession(
            "guest@example.test",
            "Gość Testowy",
            phoneNumber,
            address: null,
            documentNumber: null,
            notes: null);

        Assert.Equal("Podaj poprawny numer telefonu.", error);
    }

    [Fact]
    public void ValidateGuestSession_RejectsOversizedAndControlCharacterFields()
    {
        var oversizedAddress = new string('A', PublicAuthInputValidator.MaxAddressLength + 1);
        var addressError = PublicAuthInputValidator.ValidateGuestSession(
            "guest@example.test",
            "Gość Testowy",
            phoneNumber: null,
            oversizedAddress,
            documentNumber: null,
            notes: null);
        var documentError = PublicAuthInputValidator.ValidateGuestSession(
            "guest@example.test",
            "Gość Testowy",
            phoneNumber: null,
            address: null,
            documentNumber: "ABC\u0001XYZ",
            notes: null);

        Assert.NotNull(addressError);
        Assert.NotNull(documentError);
    }
}
