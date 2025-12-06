using SportRental.Admin.Services.Sms;
using FluentAssertions;

namespace SportRental.Admin.Tests.Services;

public class ConsoleSmsSenderEnhancedTests
{
    private readonly ConsoleSmsSender _smsSender;

    public ConsoleSmsSenderEnhancedTests()
    {
        _smsSender = new ConsoleSmsSender();
    }

    [Fact]
    public async Task SendThanksMessageAsync_WithCustomMessage_ShouldUseCustomMessage()
    {
        // Arrange
        var phoneNumber = "+48123456789";
        var customerName = "Jan Kowalski";
        var customMessage = "Dziękujemy za wybór naszej wypożyczalni!";
        
        // Capture console output
        using var sw = new StringWriter();
        Console.SetOut(sw);

        // Act
        await _smsSender.SendThanksMessageAsync(phoneNumber, customerName, customMessage);

        // Assert
        var output = sw.ToString();
        output.Should().Contain(phoneNumber);
        output.Should().Contain(customMessage);
        output.Should().NotContain("Dziękujemy Jan Kowalski za wypożyczenie"); // Should not use default
    }

    [Fact]
    public async Task SendThanksMessageAsync_WithoutCustomMessage_ShouldUseDefaultMessage()
    {
        // Arrange
        var phoneNumber = "+48987654321";
        var customerName = "Anna Nowak";
        
        // Capture console output
        using var sw = new StringWriter();
        Console.SetOut(sw);

        // Act
        await _smsSender.SendThanksMessageAsync(phoneNumber, customerName);

        // Assert
        var output = sw.ToString();
        output.Should().Contain(phoneNumber);
        output.Should().Contain(customerName);
        output.Should().Contain("Dziękujemy Anna Nowak za wypożyczenie sprzętu w SportRental!");
    }

    [Fact]
    public async Task SendReminderAsync_WithCustomMessage_ShouldUseCustomMessage()
    {
        // Arrange
        var phoneNumber = "+48111222333";
        var customerName = "Piotr Kowalczyk";
        var customMessage = "Proszę o zwrot wypożyczonego sprzętu do końca dnia.";
        
        // Capture console output
        using var sw = new StringWriter();
        Console.SetOut(sw);

        // Act
        await _smsSender.SendReminderAsync(phoneNumber, customerName, customMessage);

        // Assert
        var output = sw.ToString();
        output.Should().Contain(phoneNumber);
        output.Should().Contain(customMessage);
        output.Should().NotContain("Przypominamy Piotr Kowalczyk o zbliżającym się terminie");
    }

    [Fact]
    public async Task SendReminderAsync_WithoutCustomMessage_ShouldUseDefaultMessage()
    {
        // Arrange
        var phoneNumber = "+48444555666";
        var customerName = "Maria Testowa";
        
        // Capture console output
        using var sw = new StringWriter();
        Console.SetOut(sw);

        // Act
        await _smsSender.SendReminderAsync(phoneNumber, customerName);

        // Assert
        var output = sw.ToString();
        output.Should().Contain(phoneNumber);
        output.Should().Contain(customerName);
        output.Should().Contain("Przypominamy Maria Testowa o zbliżającym się terminie zwrotu sprzętu - SportRental");
    }

    [Fact]
    public async Task SendConfirmationRequestAsync_ShouldGenerateProperMessage()
    {
        // Arrange
        var phoneNumber = "+48777888999";
        var customerName = "Tomasz Confirmowany";
        var rentalId = Guid.NewGuid();
        
        // Capture console output
        using var sw = new StringWriter();
        Console.SetOut(sw);

        // Act
        await _smsSender.SendConfirmationRequestAsync(phoneNumber, customerName, rentalId);

        // Assert
        var output = sw.ToString();
        output.Should().Contain(phoneNumber);
        output.Should().Contain(customerName);
        output.Should().Contain("Potwierdzenie wynajmu");
        output.Should().Contain(rentalId.ToString()[..8]); // First 8 chars of GUID
        output.Should().Contain("SportRental");
        output.Should().Contain("Nie odpowiadaj na tę wiadomość");
    }

    [Theory]
    [InlineData("+48123456789")]
    [InlineData("+48 123 456 789")]
    [InlineData("123456789")]
    public async Task SendThanksMessageAsync_WithVariousPhoneFormats_ShouldWork(string phoneNumber)
    {
        // Arrange
        var customerName = "Test User";
        
        // Capture console output
        using var sw = new StringWriter();
        Console.SetOut(sw);

        // Act
        await _smsSender.SendThanksMessageAsync(phoneNumber, customerName);

        // Assert
        var output = sw.ToString();
        output.Should().Contain(phoneNumber);
        output.Should().Contain(customerName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task SendThanksMessageAsync_WithEmptyCustomMessage_ShouldUseDefault(string? customMessage)
    {
        // Arrange
        var phoneNumber = "+48123456789";
        var customerName = "Empty Test";
        
        // Capture console output
        using var sw = new StringWriter();
        Console.SetOut(sw);

        // Act
        await _smsSender.SendThanksMessageAsync(phoneNumber, customerName, customMessage);

        // Assert
        var output = sw.ToString();
        output.Should().Contain("Dziękujemy Empty Test za wypożyczenie sprzętu w SportRental!");
    }
}

// SmsApiSenderEnhancedTests zostały przeniesione do SportRental.Admin.Tests/Services/Sms/SmsApiSenderTests.cs
// z nową sygnaturą konstruktora (IOptions<SmsApiSettings>, ILogger<SmsApiSender>)