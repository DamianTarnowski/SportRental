using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using SportRental.Admin.Services.Sms;

namespace SportRental.Admin.Tests.Services.Sms;

/// <summary>
/// Testy jednostkowe dla SmsApiSender z zamockowanym klientem SMSAPI
/// </summary>
public class SmsApiSenderTests
{
    private readonly Mock<ILogger<SmsApiSender>> _loggerMock;

    public SmsApiSenderTests()
    {
        _loggerMock = new Mock<ILogger<SmsApiSender>>();
    }

    private static IOptions<SmsApiSettings> CreateSettings(
        bool isEnabled = false,
        string authToken = "",
        int sendConfirmationAttempts = 5,
        string senderName = "Test")
    {
        return Options.Create(new SmsApiSettings
        {
            IsEnabled = isEnabled,
            AuthToken = authToken,
            SendConfirmationAttempts = sendConfirmationAttempts,
            SenderName = senderName
        });
    }

    #region SendAsync Tests

    [Fact]
    public async Task SendAsync_WhenDisabled_ShouldLogToConsole()
    {
        // Arrange
        var settings = CreateSettings(isEnabled: false);
        var sender = new SmsApiSender(settings, _loggerMock.Object);
        var phoneNumber = "+48123456789";
        var message = "Test message";

        using var sw = new StringWriter();
        Console.SetOut(sw);

        // Act
        await sender.SendAsync(phoneNumber, message);

        // Assert
        var output = sw.ToString();
        output.Should().Contain("[SMS-DISABLED]");
        output.Should().Contain("123456789"); // Numer bez +48
        output.Should().Contain(message);
    }

    [Fact]
    public async Task SendAsync_WhenEnabledButNoToken_ShouldLogToConsole()
    {
        // Arrange
        var settings = CreateSettings(isEnabled: true, authToken: "");
        var sender = new SmsApiSender(settings, _loggerMock.Object);
        var phoneNumber = "123456789";
        var message = "Test message";

        using var sw = new StringWriter();
        Console.SetOut(sw);

        // Act
        await sender.SendAsync(phoneNumber, message);

        // Assert
        var output = sw.ToString();
        output.Should().Contain("[SMS-DISABLED]");
    }

    [Theory]
    [InlineData("+48123456789", "123456789")]
    [InlineData("48123456789", "123456789")]
    [InlineData("+48 123 456 789", "123456789")]
    [InlineData("123456789", "123456789")]
    [InlineData("123-456-789", "123456789")]
    [InlineData("(123) 456 789", "123456789")]
    public async Task SendAsync_ShouldNormalizePhoneNumber(string input, string expected)
    {
        // Arrange
        var settings = CreateSettings(isEnabled: false);
        var sender = new SmsApiSender(settings, _loggerMock.Object);

        using var sw = new StringWriter();
        Console.SetOut(sw);

        // Act
        await sender.SendAsync(input, "Test");

        // Assert
        var output = sw.ToString();
        output.Should().Contain(expected);
    }

    #endregion

    #region SendThanksMessageAsync Tests

    [Fact]
    public async Task SendThanksMessageAsync_WithoutCustomMessage_ShouldUseDefaultMessage()
    {
        // Arrange
        var settings = CreateSettings(isEnabled: false);
        var sender = new SmsApiSender(settings, _loggerMock.Object);
        var phoneNumber = "123456789";
        var customerName = "Jan Kowalski";

        using var sw = new StringWriter();
        Console.SetOut(sw);

        // Act
        await sender.SendThanksMessageAsync(phoneNumber, customerName);

        // Assert
        var output = sw.ToString();
        output.Should().Contain("Dziękujemy Jan Kowalski za wypożyczenie sprzętu w SportRental!");
    }

    [Fact]
    public async Task SendThanksMessageAsync_WithCustomMessage_ShouldUseCustomMessage()
    {
        // Arrange
        var settings = CreateSettings(isEnabled: false);
        var sender = new SmsApiSender(settings, _loggerMock.Object);
        var phoneNumber = "123456789";
        var customerName = "Jan Kowalski";
        var customMessage = "Dziękujemy za wizytę!";

        using var sw = new StringWriter();
        Console.SetOut(sw);

        // Act
        await sender.SendThanksMessageAsync(phoneNumber, customerName, customMessage);

        // Assert
        var output = sw.ToString();
        output.Should().Contain(customMessage);
        output.Should().NotContain("Dziękujemy Jan Kowalski za wypożyczenie");
    }

    #endregion

    #region SendReminderAsync Tests

    [Fact]
    public async Task SendReminderAsync_WithoutCustomMessage_ShouldUseDefaultMessage()
    {
        // Arrange
        var settings = CreateSettings(isEnabled: false);
        var sender = new SmsApiSender(settings, _loggerMock.Object);
        var phoneNumber = "123456789";
        var customerName = "Anna Nowak";

        using var sw = new StringWriter();
        Console.SetOut(sw);

        // Act
        await sender.SendReminderAsync(phoneNumber, customerName);

        // Assert
        var output = sw.ToString();
        output.Should().Contain("Przypominamy Anna Nowak o zbliżającym się terminie zwrotu sprzętu - SportRental");
    }

    [Fact]
    public async Task SendReminderAsync_WithCustomMessage_ShouldUseCustomMessage()
    {
        // Arrange
        var settings = CreateSettings(isEnabled: false);
        var sender = new SmsApiSender(settings, _loggerMock.Object);
        var phoneNumber = "123456789";
        var customerName = "Anna Nowak";
        var customMessage = "Proszę o zwrot sprzętu!";

        using var sw = new StringWriter();
        Console.SetOut(sw);

        // Act
        await sender.SendReminderAsync(phoneNumber, customerName, customMessage);

        // Assert
        var output = sw.ToString();
        output.Should().Contain(customMessage);
        output.Should().NotContain("Przypominamy Anna Nowak");
    }

    #endregion

    #region SendConfirmationRequestAsync Tests

    [Fact]
    public async Task SendConfirmationRequestAsync_ShouldContainRentalIdAndCustomerName()
    {
        // Arrange
        var settings = CreateSettings(isEnabled: false);
        var sender = new SmsApiSender(settings, _loggerMock.Object);
        var phoneNumber = "123456789";
        var customerName = "Piotr Testowy";
        var rentalId = Guid.NewGuid();

        using var sw = new StringWriter();
        Console.SetOut(sw);

        // Act
        await sender.SendConfirmationRequestAsync(phoneNumber, customerName, rentalId);

        // Assert
        var output = sw.ToString();
        output.Should().Contain(customerName);
        output.Should().Contain("Potwierdzenie wynajmu");
        output.Should().Contain(rentalId.ToString()[..8]);
        output.Should().Contain("SportRental");
    }

    #endregion

    #region Configuration Tests

    [Fact]
    public void Constructor_WithValidSettings_ShouldNotThrow()
    {
        // Arrange
        var settings = CreateSettings(isEnabled: true, authToken: "valid-token");

        // Act
        var act = () => new SmsApiSender(settings, _loggerMock.Object);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_WithDisabledSettings_ShouldNotCreateClient()
    {
        // Arrange
        var settings = CreateSettings(isEnabled: false, authToken: "some-token");

        // Act
        var sender = new SmsApiSender(settings, _loggerMock.Object);

        // Assert - sprawdzamy że sender nie rzuca wyjątku przy próbie wysłania
        var act = async () => await sender.SendAsync("123456789", "Test");
        act.Should().NotThrowAsync();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task SendAsync_WithEmptyOrNullMessage_ShouldStillWork(string? message)
    {
        // Arrange
        var settings = CreateSettings(isEnabled: false);
        var sender = new SmsApiSender(settings, _loggerMock.Object);

        using var sw = new StringWriter();
        Console.SetOut(sw);

        // Act
        await sender.SendAsync("123456789", message ?? "");

        // Assert
        var output = sw.ToString();
        output.Should().Contain("[SMS-DISABLED]");
        output.Should().Contain("123456789");
    }

    #endregion

    #region Logger Verification Tests

    [Fact]
    public async Task SendAsync_WhenDisabled_ShouldLogInformation()
    {
        // Arrange
        var settings = CreateSettings(isEnabled: false);
        var sender = new SmsApiSender(settings, _loggerMock.Object);

        using var sw = new StringWriter();
        Console.SetOut(sw);

        // Act
        await sender.SendAsync("123456789", "Test message");

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("[SMS-DISABLED]")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion
}

/// <summary>
/// Testy dla modelu SmsApiSettings
/// </summary>
public class SmsApiSettingsTests
{
    [Fact]
    public void SectionName_ShouldBe_smsApi()
    {
        // Assert
        SmsApiSettings.SectionName.Should().Be("smsApi");
    }

    [Fact]
    public void DefaultValues_ShouldBeCorrect()
    {
        // Arrange & Act
        var settings = new SmsApiSettings();

        // Assert
        settings.AuthToken.Should().BeEmpty();
        settings.IsEnabled.Should().BeFalse();
        settings.SendConfirmationAttempts.Should().Be(5);
        settings.SenderName.Should().Be("Test");
    }
}

/// <summary>
/// Testy dla modelu SmsDeliveryReport
/// </summary>
public class SmsDeliveryReportTests
{
    [Fact]
    public void SmsDeliveryReport_ShouldHaveAllProperties()
    {
        // Arrange & Act
        var report = new SmsDeliveryReport
        {
            MsgId = "msg123",
            Status = SmsDeliveryStatus.Delivered,
            To = "123456789",
            DoneDate = "2025-11-30 12:00:00",
            Idx = "idx123",
            Username = "testuser",
            Parts = 1
        };

        // Assert
        report.MsgId.Should().Be("msg123");
        report.Status.Should().Be("DELIVERED");
        report.To.Should().Be("123456789");
        report.DoneDate.Should().Be("2025-11-30 12:00:00");
        report.Idx.Should().Be("idx123");
        report.Username.Should().Be("testuser");
        report.Parts.Should().Be(1);
    }

    [Fact]
    public void SmsDeliveryStatus_ShouldHaveCorrectValues()
    {
        // Assert
        SmsDeliveryStatus.Delivered.Should().Be("DELIVERED");
        SmsDeliveryStatus.Undelivered.Should().Be("UNDELIVERED");
        SmsDeliveryStatus.Expired.Should().Be("EXPIRED");
        SmsDeliveryStatus.Sent.Should().Be("SENT");
        SmsDeliveryStatus.Unknown.Should().Be("UNKNOWN");
        SmsDeliveryStatus.Rejected.Should().Be("REJECTED");
        SmsDeliveryStatus.Pending.Should().Be("PENDING");
    }
}

