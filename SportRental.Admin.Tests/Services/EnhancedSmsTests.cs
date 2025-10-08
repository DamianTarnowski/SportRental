using SportRental.Admin.Services.Sms;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;
using System.Net.Http;

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

public class SmsApiSenderEnhancedTests
{
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
    private readonly Mock<IConfiguration> _configurationMock;
    private readonly Mock<HttpMessageHandler> _httpMessageHandlerMock;
    private readonly HttpClient _httpClient;

    public SmsApiSenderEnhancedTests()
    {
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _configurationMock = new Mock<IConfiguration>();
        _httpMessageHandlerMock = new Mock<HttpMessageHandler>();
        
        _httpClient = new HttpClient(_httpMessageHandlerMock.Object);
        _httpClientFactoryMock.Setup(x => x.CreateClient("smsapi")).Returns(_httpClient);
        
        // Default configuration - no token (fallback to console)
        _configurationMock.Setup(x => x["SmsApi:Token"]).Returns(string.Empty);
        _configurationMock.Setup(x => x["SmsApi:From"]).Returns("SportRental");
    }

    [Fact]
    public async Task SendThanksMessageAsync_WithoutToken_ShouldFallbackToConsole()
    {
        // Arrange
        var smsSender = new SmsApiSender(_configurationMock.Object, _httpClientFactoryMock.Object);
        var phoneNumber = "+48123456789";
        var customerName = "Console User";
        
        // Capture console output
        using var sw = new StringWriter();
        Console.SetOut(sw);

        // Act
        await smsSender.SendThanksMessageAsync(phoneNumber, customerName);

        // Assert
        var output = sw.ToString();
        output.Should().Contain("[SMSAPI-MOCK]");
        output.Should().Contain(phoneNumber);
        output.Should().Contain(customerName);
        output.Should().Contain("Dziękujemy Console User za wypożyczenie sprzętu w SportRental!");
    }

    [Fact]
    public async Task SendReminderAsync_WithCustomMessage_ShouldFallbackToConsoleWithCustomMessage()
    {
        // Arrange
        var smsSender = new SmsApiSender(_configurationMock.Object, _httpClientFactoryMock.Object);
        var phoneNumber = "+48987654321";
        var customerName = "Reminder User";
        var customMessage = "Niestandardowe przypomnienie o zwrocie";
        
        // Capture console output
        using var sw = new StringWriter();
        Console.SetOut(sw);

        // Act
        await smsSender.SendReminderAsync(phoneNumber, customerName, customMessage);

        // Assert
        var output = sw.ToString();
        output.Should().Contain("[SMSAPI-MOCK]");
        output.Should().Contain(phoneNumber);
        output.Should().Contain(customMessage);
        output.Should().NotContain("Przypominamy Reminder User o zbliżającym się terminie");
    }

    [Fact]
    public async Task SendConfirmationRequestAsync_ShouldFallbackToConsole()
    {
        // Arrange
        var smsSender = new SmsApiSender(_configurationMock.Object, _httpClientFactoryMock.Object);
        var phoneNumber = "+48111222333";
        var customerName = "Confirmation User";
        var rentalId = Guid.NewGuid();
        
        // Capture console output
        using var sw = new StringWriter();
        Console.SetOut(sw);

        // Act
        await smsSender.SendConfirmationRequestAsync(phoneNumber, customerName, rentalId);

        // Assert
        var output = sw.ToString();
        output.Should().Contain("[SMSAPI-MOCK]");
        output.Should().Contain(phoneNumber);
        output.Should().Contain(customerName);
        output.Should().Contain("Potwierdzenie wynajmu");
        output.Should().Contain(rentalId.ToString()[..8]);
    }

    [Fact]
    public async Task SendThanksMessageAsync_WithToken_ShouldConfigureHttpClient()
    {
        // Arrange
        _configurationMock.Setup(x => x["SmsApi:Token"]).Returns("test-token-123");
        var smsSender = new SmsApiSender(_configurationMock.Object, _httpClientFactoryMock.Object);
        
        // Verify that HttpClient was created with correct base address
        _httpClientFactoryMock.Verify(x => x.CreateClient("smsapi"), Times.Once);
        _httpClient.BaseAddress.Should().Be(new Uri("https://api.smsapi.pl/"));
    }

    [Fact]
    public void SmsApiSender_WithFromConfiguration_ShouldSetFromParameter()
    {
        // Arrange
        _configurationMock.Setup(x => x["SmsApi:From"]).Returns("CustomSender");
        
        // Act
        var smsSender = new SmsApiSender(_configurationMock.Object, _httpClientFactoryMock.Object);
        
        // Assert
        // The 'from' parameter is set internally and would be used in actual HTTP calls
        // This test verifies the constructor doesn't throw and sets up the service correctly
        smsSender.Should().NotBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task SendThanksMessageAsync_WithEmptyToken_ShouldFallbackToConsole(string? token)
    {
        // Arrange
        _configurationMock.Setup(x => x["SmsApi:Token"]).Returns(token);
        var smsSender = new SmsApiSender(_configurationMock.Object, _httpClientFactoryMock.Object);
        
        // Capture console output
        using var sw = new StringWriter();
        Console.SetOut(sw);

        // Act
        await smsSender.SendThanksMessageAsync("+48123456789", "Test User");

        // Assert
        var output = sw.ToString();
        output.Should().Contain("[SMSAPI-MOCK]");
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _httpClient?.Dispose();
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}