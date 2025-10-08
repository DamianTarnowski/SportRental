using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using SportRental.Api.Services.Email;
using Xunit;
using Xunit.Abstractions;

namespace SportRental.Api.Tests;

/// <summary>
/// Integration tests for email sending using real Onet SMTP
/// </summary>
public class EmailIntegrationTests
{
    private readonly ITestOutputHelper _output;
    private readonly IConfiguration _configuration;

    public EmailIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        
        // Load configuration from appsettings.Test.json
        _configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.Test.json", optional: false)
            .Build();
    }

    [Fact]
    public async Task SendEmail_WithOnetSMTP_Succeeds()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<SmtpEmailSender>>();
        var emailSender = new SmtpEmailSender(_configuration, loggerMock.Object);

        var testRecipient = _configuration["TestAccounts:TestCustomer:Email"] ?? "testklient@op.pl";
        var subject = "Test Email - SportRental";
        var htmlBody = @"
            <html>
            <body style='font-family: Arial, sans-serif;'>
                <h2 style='color: #667eea;'>🎉 Test Email z SportRental!</h2>
                <p>To jest testowa wiadomość wysłana z systemu SportRental.</p>
                <p><strong>Data wysłania:</strong> " + DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss") + @"</p>
                <hr>
                <p style='color: #666; font-size: 12px;'>
                    Ten email został wysłany automatycznie przez system testowy.<br>
                    SMTP: smtp.poczta.onet.pl (Onet)
                </p>
            </body>
            </html>";

        // Act
        var exception = await Record.ExceptionAsync(async () =>
        {
            await emailSender.SendEmailAsync(testRecipient, subject, htmlBody);
        });

        // Assert
        exception.Should().BeNull("Email should be sent successfully");
        _output.WriteLine($"✅ Email sent successfully to {testRecipient}");
        _output.WriteLine($"   Subject: {subject}");
        _output.WriteLine($"   SMTP: smtp.poczta.onet.pl");
    }

    [Fact(Skip = "Integration test - only run manually")]
    public async Task SendEmail_ToMultipleRecipients_Succeeds()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<SmtpEmailSender>>();
        var emailSender = new SmtpEmailSender(_configuration, loggerMock.Object);

        var recipients = new[]
        {
            _configuration["TestAccounts:TestCustomer:Email"] ?? "testklient@op.pl",
            _configuration["TestAccounts:RentalOwner:Email"] ?? "contact.sportrental@op.pl"
        };

        var subject = "Test Multiple Recipients - SportRental";
        var htmlBody = "<h2>Test email to multiple recipients</h2><p>This is a test.</p>";

        // Act & Assert
        foreach (var recipient in recipients)
        {
            var exception = await Record.ExceptionAsync(async () =>
            {
                await emailSender.SendEmailAsync(recipient, subject, htmlBody);
            });

            exception.Should().BeNull($"Email to {recipient} should be sent successfully");
            _output.WriteLine($"✅ Email sent to {recipient}");
        }
    }

    [Fact(Skip = "Integration test - only run manually")]
    public async Task SendEmail_WithInvalidCredentials_ThrowsException()
    {
        // Arrange
        var invalidConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Email:Smtp:Enabled"] = "true",
                ["Email:Smtp:Host"] = "smtp.poczta.onet.pl",
                ["Email:Smtp:Port"] = "465",
                ["Email:Smtp:EnableSsl"] = "true",
                ["Email:Smtp:Username"] = "invalid@op.pl",
                ["Email:Smtp:Password"] = "WrongPassword123",
                ["Email:Smtp:SenderEmail"] = "invalid@op.pl",
                ["Email:Smtp:SenderName"] = "Test"
            })
            .Build();

        var loggerMock = new Mock<ILogger<SmtpEmailSender>>();
        var emailSender = new SmtpEmailSender(invalidConfig, loggerMock.Object);

        // Act
        var exception = await Record.ExceptionAsync(async () =>
        {
            await emailSender.SendEmailAsync("test@example.com", "Test", "Test body");
        });

        // Assert
        exception.Should().NotBeNull("Should throw exception with invalid credentials");
        _output.WriteLine($"✅ Exception thrown as expected: {exception?.GetType().Name}");
    }

    [Fact]
    public void Configuration_HasValidOnetSettings()
    {
        // Arrange & Act
        var host = _configuration["Email:Smtp:Host"];
        var port = _configuration["Email:Smtp:Port"];
        var enableSsl = _configuration["Email:Smtp:EnableSsl"];
        var username = _configuration["Email:Smtp:Username"];

        // Assert
        host.Should().Be("smtp.poczta.onet.pl", "Should use Onet SMTP server");
        port.Should().Be("465", "Onet uses port 465 for SSL");
        // Note: Port is now int, EnableSsl is now bool (not string)
        var sslValue = enableSsl?.ToLowerInvariant();
        sslValue.Should().BeOneOf("true", "True", "TRUE", "SSL should be enabled for Onet");
        username.Should().EndWith("@op.pl", "Should be valid Onet email address");

        _output.WriteLine("✅ Configuration validated:");
        _output.WriteLine($"   Host: {host}");
        _output.WriteLine($"   Port: {port}");
        _output.WriteLine($"   SSL: {enableSsl}");
        _output.WriteLine($"   Username: {username}");
    }

    [Fact]
    public void TestAccounts_AreConfigured()
    {
        // Arrange & Act
        var rentalOwnerEmail = _configuration["TestAccounts:RentalOwner:Email"];
        var testCustomerEmail = _configuration["TestAccounts:TestCustomer:Email"];

        // Assert
        rentalOwnerEmail.Should().NotBeNullOrEmpty("Rental owner email should be configured");
        testCustomerEmail.Should().NotBeNullOrEmpty("Test customer email should be configured");

        rentalOwnerEmail.Should().Be("contact.sportrental@op.pl");
        testCustomerEmail.Should().Be("testklient@op.pl");

        _output.WriteLine("✅ Test accounts configured:");
        _output.WriteLine($"   Rental Owner: {rentalOwnerEmail}");
        _output.WriteLine($"   Test Customer: {testCustomerEmail}");
    }
}
