using SportRental.Admin.Services.Email;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace SportRental.Admin.Tests.Services.Email;

public class SmtpEmailSenderTests
{
    private readonly Mock<IConfiguration> _configurationMock;
    private readonly Mock<ILogger<SmtpEmailSender>> _loggerMock;
    private readonly SmtpEmailSender _emailSender;

    public SmtpEmailSenderTests()
    {
        _configurationMock = new Mock<IConfiguration>();
        _loggerMock = new Mock<ILogger<SmtpEmailSender>>();
        
        // Setup mock configuration
        _configurationMock.Setup(c => c["Email:Host"]).Returns("smtp.test.com");
        _configurationMock.Setup(c => c["Email:Port"]).Returns("587");
        _configurationMock.Setup(c => c["Email:Username"]).Returns("test@test.com");
        _configurationMock.Setup(c => c["Email:Password"]).Returns("testpass");
        _configurationMock.Setup(c => c["Email:FromName"]).Returns("Sport Rental System");
        
        _emailSender = new SmtpEmailSender(_configurationMock.Object, _loggerMock.Object);
    }

    [Fact]
    public void Constructor_WithValidConfiguration_ShouldInitialize()
    {
        // Act & Assert
        _emailSender.Should().NotBeNull();
    }

    [Theory]
    [InlineData("", "subject", "message")]
    [InlineData(null, "subject", "message")]
    [InlineData("test@test.com", "", "message")]
    [InlineData("test@test.com", null, "message")]
    [InlineData("test@test.com", "subject", "")]
    [InlineData("test@test.com", "subject", null)]
    public async Task SendEmailAsync_WithInvalidParameters_ShouldThrowArgumentException(
        string email, string subject, string message)
    {
        // Act & Assert
        await _emailSender.Invoking(x => x.SendEmailAsync(email, subject, message))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SendEmailAsync_WithValidParameters_ShouldAttemptConnection()
    {
        // Arrange
        var email = "recipient@test.com";
        var subject = "Test Subject";
        var message = "Test Message";

        // Act & Assert - Since we can't actually send emails in tests, 
        // we verify that the method attempts to connect (and fails as expected)
        var exception = await _emailSender.Invoking(x => x.SendEmailAsync(email, subject, message))
            .Should().ThrowAsync<Exception>();
        
        // Expected connection failure in test environment
        exception.Which.Should().BeOfType<System.Net.Sockets.SocketException>();
    }

    [Theory]
    [InlineData("invalid-email")]
    [InlineData("@test.com")]
    [InlineData("test@")]
    [InlineData("test.com")]
    public async Task SendEmailAsync_WithInvalidEmailFormat_ShouldThrowArgumentException(string invalidEmail)
    {
        // Act & Assert
        await _emailSender.Invoking(x => x.SendEmailAsync(invalidEmail, "subject", "message"))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SendRentalContractAsync_WithValidParameters_ShouldAttemptConnection()
    {
        // Arrange
        var email = "customer@test.com";
        var customerName = "Jan Kowalski";
        var contractPdf = new byte[] { 1, 2, 3, 4, 5 }; // Mock PDF data

        // Act & Assert
        var exception = await _emailSender.Invoking(x => x.SendRentalContractAsync(email, customerName, contractPdf))
            .Should().ThrowAsync<Exception>();
            
        // Expected connection failure in test environment
        exception.Which.Should().BeOfType<System.Net.Sockets.SocketException>();
    }

    [Theory]
    [InlineData("", "Jan Kowalski")]
    [InlineData(null, "Jan Kowalski")]
    [InlineData("test@test.com", "")]
    [InlineData("test@test.com", null)]
    public async Task SendRentalContractAsync_WithInvalidParameters_ShouldThrowArgumentException(
        string email, string customerName)
    {
        // Arrange
        var contractPdf = new byte[] { 1, 2, 3 };

        // Act & Assert
        await _emailSender.Invoking(x => x.SendRentalContractAsync(email, customerName, contractPdf))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SendRentalContractAsync_WithNullOrEmptyPdf_ShouldThrowArgumentException()
    {
        // Arrange
        var email = "test@test.com";
        var customerName = "Jan Kowalski";

        // Act & Assert
        await _emailSender.Invoking(x => x.SendRentalContractAsync(email, customerName, null!))
            .Should().ThrowAsync<ArgumentException>();

        await _emailSender.Invoking(x => x.SendRentalContractAsync(email, customerName, Array.Empty<byte>()))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SendReminderAsync_WithValidParameters_ShouldAttemptConnection()
    {
        // Arrange
        var email = "customer@test.com";
        var customerName = "Jan Kowalski";
        var reminderText = "Przypomnienie o zwrocie sprzętu";

        // Act & Assert
        var exception = await _emailSender.Invoking(x => x.SendReminderAsync(email, customerName, reminderText))
            .Should().ThrowAsync<Exception>();
            
        // Expected connection failure in test environment
        exception.Which.Should().BeOfType<System.Net.Sockets.SocketException>();
    }

    [Theory]
    [InlineData("", "Jan Kowalski", "reminder")]
    [InlineData(null, "Jan Kowalski", "reminder")]
    [InlineData("test@test.com", "", "reminder")]
    [InlineData("test@test.com", null, "reminder")]
    [InlineData("test@test.com", "Jan Kowalski", "")]
    [InlineData("test@test.com", "Jan Kowalski", null)]
    public async Task SendReminderAsync_WithInvalidParameters_ShouldThrowArgumentException(
        string email, string customerName, string reminderText)
    {
        // Act & Assert
        await _emailSender.Invoking(x => x.SendReminderAsync(email, customerName, reminderText))
            .Should().ThrowAsync<ArgumentException>();
    }
}