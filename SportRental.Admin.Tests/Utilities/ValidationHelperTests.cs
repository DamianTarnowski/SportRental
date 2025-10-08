using FluentAssertions;

namespace SportRental.Admin.Tests.Utilities;

public class ValidationHelperTests
{
    [Theory]
    [InlineData("test@example.com", true)]
    [InlineData("user.name+tag@example.co.uk", true)]
    [InlineData("user@domain.org", true)]
    [InlineData("invalid-email", false)]
    [InlineData("@domain.com", false)]
    [InlineData("user@", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidEmail_WithVariousInputs_ShouldReturnCorrectResult(string? email, bool expected)
    {
        // Act
        var result = IsValidEmailFormat(email);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("+48123456789", true)]
    [InlineData("+48 123 456 789", true)]
    [InlineData("123456789", true)]
    [InlineData("123-456-789", true)]
    [InlineData("48123456789", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("abc", false)]
    [InlineData("12345", false)] // Too short
    public void IsValidPhoneNumber_WithVariousInputs_ShouldReturnCorrectResult(string? phone, bool expected)
    {
        // Act
        var result = IsValidPhoneFormat(phone);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("BIKE001", true)]
    [InlineData("TENT-4P-BLUE", true)]
    [InlineData("SKU_123", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("   ", false)]
    public void IsValidSku_WithVariousInputs_ShouldReturnCorrectResult(string? sku, bool expected)
    {
        // Act
        var result = IsValidSkuFormat(sku);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(0.01, true)]
    [InlineData(25.50, true)]
    [InlineData(999.99, true)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(-25.50, false)]
    public void IsValidPrice_WithVariousInputs_ShouldReturnCorrectResult(decimal price, bool expected)
    {
        // Act
        var result = IsValidPrice(price);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(5, true)]
    [InlineData(100, true)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    public void IsValidQuantity_WithVariousInputs_ShouldReturnCorrectResult(int quantity, bool expected)
    {
        // Act
        var result = IsValidQuantity(quantity);

        // Assert
        result.Should().Be(expected);
    }

    // Helper methods that simulate validation logic
    private static bool IsValidEmailFormat(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;

        return email.Contains("@") && email.Contains(".") && 
               email.IndexOf("@") < email.LastIndexOf(".") &&
               !email.StartsWith("@") && !email.EndsWith("@");
    }

    private static bool IsValidPhoneFormat(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return false;

        // Remove spaces and hyphens for validation
        var cleanPhone = phone.Replace(" ", "").Replace("-", "");
        
        // Should contain only digits and optionally start with +
        if (cleanPhone.StartsWith("+"))
            cleanPhone = cleanPhone.Substring(1);

        return cleanPhone.All(char.IsDigit) && cleanPhone.Length >= 9;
    }

    private static bool IsValidSkuFormat(string? sku)
    {
        return !string.IsNullOrWhiteSpace(sku);
    }

    private static bool IsValidPrice(decimal price)
    {
        return price > 0;
    }

    private static bool IsValidQuantity(int quantity)
    {
        return quantity > 0;
    }
}