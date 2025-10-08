using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using SportRental.Api.Services.Contracts;
using SportRental.Api.Services.Email;
using SportRental.Infrastructure.Domain;
using Xunit;
using Xunit.Abstractions;

namespace SportRental.Api.Tests;

/// <summary>
/// REAL integration tests that actually send emails through Onet SMTP
/// These tests verify the complete email flow with real credentials
/// </summary>
public class OnetEmailRealTests
{
    private readonly ITestOutputHelper _output;
    private readonly IConfiguration _configuration;

    public OnetEmailRealTests(ITestOutputHelper output)
    {
        _output = output;
        
        _configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.Test.json", optional: false)
            .Build();
    }

    [Fact]
    public async Task RealTest_SendSimpleEmail_ThroughOnetSMTP()
    {
        // Arrange
        _output.WriteLine("═══════════════════════════════════════");
        _output.WriteLine("📧 TEST 1: Sending Simple Email");
        _output.WriteLine("═══════════════════════════════════════");
        _output.WriteLine("");

        var loggerMock = new Mock<ILogger<SmtpEmailSender>>();
        var emailSender = new SmtpEmailSender(_configuration, loggerMock.Object);

        var testRecipient = _configuration["TestAccounts:TestCustomer:Email"] ?? "testklient@op.pl";
        var senderEmail = _configuration["Email:Smtp:SenderEmail"] ?? "contact.sportrental@op.pl";
        
        var subject = $"✅ Test Email - SportRental [{DateTime.Now:HH:mm:ss}]";
        var htmlBody = $@"
            <!DOCTYPE html>
            <html>
            <head>
                <meta charset='UTF-8'>
            </head>
            <body style='font-family: Arial, sans-serif; padding: 20px; background-color: #f5f5f5;'>
                <div style='max-width: 600px; margin: 0 auto; background-color: white; padding: 30px; border-radius: 8px; box-shadow: 0 2px 4px rgba(0,0,0,0.1);'>
                    <h1 style='color: #667eea; text-align: center;'>🎉 Test Email - SportRental!</h1>
                    <hr style='border: none; border-top: 2px solid #667eea; margin: 20px 0;'>
                    
                    <h2 style='color: #333;'>To jest testowa wiadomość</h2>
                    <p style='font-size: 16px; line-height: 1.6; color: #555;'>
                        Ten email został wysłany automatycznie przez system testowy SportRental 
                        w celu weryfikacji konfiguracji SMTP Onet.
                    </p>
                    
                    <div style='background-color: #f9fafb; padding: 15px; border-left: 4px solid #667eea; margin: 20px 0;'>
                        <p style='margin: 5px 0;'><strong>📅 Data wysłania:</strong> {DateTime.Now:dd.MM.yyyy HH:mm:ss}</p>
                        <p style='margin: 5px 0;'><strong>📧 Od:</strong> {senderEmail}</p>
                        <p style='margin: 5px 0;'><strong>👤 Do:</strong> {testRecipient}</p>
                        <p style='margin: 5px 0;'><strong>🌐 SMTP:</strong> smtp.poczta.onet.pl:465 (SSL)</p>
                    </div>
                    
                    <div style='margin-top: 30px; padding-top: 20px; border-top: 1px solid #e5e7eb;'>
                        <p style='color: #666; font-size: 14px; text-align: center;'>
                            ✅ Jeśli widzisz ten email, to konfiguracja SMTP działa poprawnie!
                        </p>
                    </div>
                    
                    <div style='margin-top: 20px; text-align: center; color: #999; font-size: 12px;'>
                        <p>SportRental - System wypożyczalni sprzętu sportowego</p>
                        <p>Test email - można bezpiecznie usunąć</p>
                    </div>
                </div>
            </body>
            </html>";

        _output.WriteLine($"From:    {senderEmail}");
        _output.WriteLine($"To:      {testRecipient}");
        _output.WriteLine($"Subject: {subject}");
        _output.WriteLine("");

        // Act
        _output.WriteLine("📤 Sending email...");
        var startTime = DateTime.Now;
        
        var exception = await Record.ExceptionAsync(async () =>
        {
            await emailSender.SendEmailAsync(testRecipient, subject, htmlBody);
        });

        var elapsed = DateTime.Now - startTime;
        
        // Assert
        exception.Should().BeNull("Email should be sent successfully without exceptions");
        
        _output.WriteLine("");
        _output.WriteLine("═══════════════════════════════════════");
        _output.WriteLine($"✅ SUCCESS! Email sent in {elapsed.TotalSeconds:F2}s");
        _output.WriteLine("═══════════════════════════════════════");
        _output.WriteLine("");
        _output.WriteLine("📬 CHECK YOUR INBOX:");
        _output.WriteLine($"   URL:   https://poczta.onet.pl");
        _output.WriteLine($"   Login: {testRecipient}");
        _output.WriteLine($"   Look for: {subject}");
        _output.WriteLine("");
    }

    [Fact]
    public async Task RealTest_SendEmailWithPdfAttachment_ThroughOnet()
    {
        // Arrange
        _output.WriteLine("═══════════════════════════════════════");
        _output.WriteLine("📄 TEST 2: Email with PDF Attachment");
        _output.WriteLine("═══════════════════════════════════════");
        _output.WriteLine("");

        var emailLoggerMock = new Mock<ILogger<SmtpEmailSender>>();
        var emailSender = new SmtpEmailSender(_configuration, emailLoggerMock.Object);

        var pdfLoggerMock = new Mock<ILogger<PdfContractService>>();
        var envMock = new Mock<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>();
        envMock.Setup(e => e.WebRootPath).Returns(Path.GetTempPath());
        var pdfService = new PdfContractService(envMock.Object, pdfLoggerMock.Object);

        var confirmationLoggerMock = new Mock<ILogger<RentalConfirmationEmailService>>();
        var confirmationService = new RentalConfirmationEmailService(
            emailSender, 
            pdfService, 
            confirmationLoggerMock.Object);

        // Create test data
        var tenantId = Guid.NewGuid();
        var testEmail = _configuration["TestAccounts:TestCustomer:Email"] ?? "testklient@op.pl";
        
        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            FullName = "Jan Testowy",
            Email = testEmail,
            PhoneNumber = "+48 123 456 789",
            DocumentNumber = "TEST123456"
        };

        var rental = new Rental
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CustomerId = customer.Id,
            Customer = customer,
            StartDateUtc = DateTime.UtcNow.AddDays(2),
            EndDateUtc = DateTime.UtcNow.AddDays(5),
            TotalAmount = 1080m,
            DepositAmount = 324m,
            Status = RentalStatus.Confirmed,
            PaymentStatus = "Succeeded"
        };

        var items = new List<(Product product, int quantity)>
        {
            (new Product
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Name = "Narty Rossignol Hero Elite ST Ti",
                DailyPrice = 120m,
                Description = "Profesjonalne narty zjazdowe"
            }, 3)
        };

        _output.WriteLine($"To:          {customer.Email}");
        _output.WriteLine($"Customer:    {customer.FullName}");
        _output.WriteLine($"Rental ID:   {rental.Id.ToString()[..8]}");
        _output.WriteLine($"Period:      {(rental.EndDateUtc - rental.StartDateUtc).Days} days");
        _output.WriteLine($"Total:       {rental.TotalAmount} PLN");
        _output.WriteLine($"Deposit:     {rental.DepositAmount} PLN");
        _output.WriteLine($"Products:    {items.Count}");
        _output.WriteLine("");

        // Act
        _output.WriteLine("📤 Generating PDF contract...");
        var startTime = DateTime.Now;

        var exception = await Record.ExceptionAsync(async () =>
        {
            await confirmationService.SendRentalConfirmationAsync(
                customer.Email,
                customer.FullName,
                customer,
                rental,
                items);
        });

        var elapsed = DateTime.Now - startTime;

        // Assert
        exception.Should().BeNull("Email with PDF attachment should be sent successfully");

        _output.WriteLine("");
        _output.WriteLine("═══════════════════════════════════════");
        _output.WriteLine($"✅ SUCCESS! Email+PDF sent in {elapsed.TotalSeconds:F2}s");
        _output.WriteLine("═══════════════════════════════════════");
        _output.WriteLine("");
        _output.WriteLine("📬 CHECK YOUR INBOX:");
        _output.WriteLine($"   URL:   https://poczta.onet.pl");
        _output.WriteLine($"   Login: {testEmail}");
        _output.WriteLine($"   Look for: Potwierdzenie wypożyczenia");
        _output.WriteLine($"   Attachment: umowa_*.pdf");
        _output.WriteLine("");
        _output.WriteLine("📄 PDF Should contain:");
        _output.WriteLine("   • Customer data (name, email, phone)");
        _output.WriteLine("   • Rental period (start, end, days)");
        _output.WriteLine("   • Product table (Narty Rossignol × 3)");
        _output.WriteLine("   • Financial summary (total, deposit)");
        _output.WriteLine("   • Terms & conditions");
        _output.WriteLine("   • Signature spaces");
        _output.WriteLine("");
    }

    [Fact]
    public async Task RealTest_SendToMultipleRecipients_BothOnetAccounts()
    {
        // Arrange
        _output.WriteLine("═══════════════════════════════════════");
        _output.WriteLine("👥 TEST 3: Multiple Recipients");
        _output.WriteLine("═══════════════════════════════════════");
        _output.WriteLine("");

        var loggerMock = new Mock<ILogger<SmtpEmailSender>>();
        var emailSender = new SmtpEmailSender(_configuration, loggerMock.Object);

        var recipients = new[]
        {
            (_configuration["TestAccounts:TestCustomer:Email"] ?? "testklient@op.pl", "Klient Testowy"),
            (_configuration["TestAccounts:RentalOwner:Email"] ?? "contact.sportrental@op.pl", "Wypożyczalnia")
        };

        var subject = $"📧 Multi-recipient Test - SportRental [{DateTime.Now:HH:mm:ss}]";
        var htmlBody = $@"
            <!DOCTYPE html>
            <html>
            <body style='font-family: Arial; padding: 20px;'>
                <h2 style='color: #667eea;'>✅ Test wielokrotnego wysyłania</h2>
                <p>Ten email został wysłany do wielu odbiorców jednocześnie.</p>
                <div style='background: #f0f0f0; padding: 15px; margin: 20px 0;'>
                    <p><strong>Wysłano:</strong> {DateTime.Now:dd.MM.yyyy HH:mm:ss}</p>
                    <p><strong>Test ID:</strong> {Guid.NewGuid().ToString()[..8]}</p>
                </div>
                <p style='color: #666; font-size: 12px;'>SportRental - System testowy</p>
            </body>
            </html>";

        _output.WriteLine("Recipients:");
        foreach (var (email, name) in recipients)
        {
            _output.WriteLine($"  • {name}: {email}");
        }
        _output.WriteLine("");

        // Act & Assert
        var results = new List<(string email, bool success, TimeSpan elapsed)>();
        
        foreach (var (email, name) in recipients)
        {
            _output.WriteLine($"📤 Sending to {name}...");
            var startTime = DateTime.Now;
            
            var exception = await Record.ExceptionAsync(async () =>
            {
                await emailSender.SendEmailAsync(email, subject, htmlBody);
            });

            var elapsed = DateTime.Now - startTime;
            var success = exception == null;
            
            results.Add((email, success, elapsed));
            
            if (success)
            {
                _output.WriteLine($"   ✅ Sent successfully in {elapsed.TotalSeconds:F2}s");
            }
            else
            {
                _output.WriteLine($"   ❌ Failed: {exception?.Message}");
            }
        }

        // Assert
        results.Should().AllSatisfy(r => r.success.Should().BeTrue($"Email to {r.email} should be sent successfully"));
        
        var totalTime = results.Sum(r => r.elapsed.TotalSeconds);
        var avgTime = totalTime / results.Count;

        _output.WriteLine("");
        _output.WriteLine("═══════════════════════════════════════");
        _output.WriteLine($"✅ SUCCESS! All {results.Count} emails sent");
        _output.WriteLine($"   Total time: {totalTime:F2}s");
        _output.WriteLine($"   Avg time:   {avgTime:F2}s/email");
        _output.WriteLine("═══════════════════════════════════════");
        _output.WriteLine("");
    }

    [Fact]
    public async Task RealTest_SendComplexEmail_WithFullHtml()
    {
        // Arrange
        _output.WriteLine("═══════════════════════════════════════");
        _output.WriteLine("🎨 TEST 4: Complex HTML Email");
        _output.WriteLine("═══════════════════════════════════════");
        _output.WriteLine("");

        var loggerMock = new Mock<ILogger<SmtpEmailSender>>();
        var emailSender = new SmtpEmailSender(_configuration, loggerMock.Object);

        var testRecipient = _configuration["TestAccounts:TestCustomer:Email"] ?? "testklient@op.pl";
        var subject = $"🎨 Complex HTML Test - SportRental [{DateTime.Now:HH:mm:ss}]";
        
        var htmlBody = GenerateComplexHtmlEmail();

        _output.WriteLine($"To:      {testRecipient}");
        _output.WriteLine($"Subject: {subject}");
        _output.WriteLine($"HTML size: {htmlBody.Length} chars");
        _output.WriteLine("");

        // Act
        _output.WriteLine("📤 Sending complex HTML email...");
        var startTime = DateTime.Now;
        
        var exception = await Record.ExceptionAsync(async () =>
        {
            await emailSender.SendEmailAsync(testRecipient, subject, htmlBody);
        });

        var elapsed = DateTime.Now - startTime;

        // Assert
        exception.Should().BeNull("Complex HTML email should be sent successfully");

        _output.WriteLine("");
        _output.WriteLine("═══════════════════════════════════════");
        _output.WriteLine($"✅ SUCCESS! Complex email sent in {elapsed.TotalSeconds:F2}s");
        _output.WriteLine("═══════════════════════════════════════");
        _output.WriteLine("");
        _output.WriteLine("📬 Email should contain:");
        _output.WriteLine("   • Gradient header");
        _output.WriteLine("   • Product table");
        _output.WriteLine("   • Financial summary");
        _output.WriteLine("   • Colored sections");
        _output.WriteLine("   • Responsive layout");
        _output.WriteLine("");
    }

    private string GenerateComplexHtmlEmail()
    {
        return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='UTF-8'>
    <style>
        body {{ font-family: Arial, sans-serif; background-color: #f5f5f5; padding: 20px; }}
        .container {{ max-width: 600px; margin: 0 auto; background: white; }}
        .header {{ background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; padding: 30px; text-align: center; }}
        .content {{ padding: 30px; }}
        .table {{ width: 100%; border-collapse: collapse; margin: 20px 0; }}
        .table th {{ background: #f3f4f6; padding: 12px; text-align: left; }}
        .table td {{ padding: 12px; border-bottom: 1px solid #e5e7eb; }}
        .summary {{ background: #f9fafb; padding: 20px; margin: 20px 0; }}
        .footer {{ background: #f9fafb; padding: 20px; text-align: center; color: #666; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>🎨 Complex HTML Email Test</h1>
            <p>SportRental System</p>
        </div>
        
        <div class='content'>
            <h2>Test Zaawansowanego HTML</h2>
            <p>Ten email testuje renderowanie złożonego HTML z tabelami, kolorami i layoutem.</p>
            
            <table class='table'>
                <thead>
                    <tr>
                        <th>Produkt</th>
                        <th>Ilość</th>
                        <th>Cena</th>
                    </tr>
                </thead>
                <tbody>
                    <tr>
                        <td>Narty Test</td>
                        <td>2</td>
                        <td>240 zł</td>
                    </tr>
                    <tr>
                        <td>Buty Test</td>
                        <td>2</td>
                        <td>80 zł</td>
                    </tr>
                </tbody>
            </table>
            
            <div class='summary'>
                <h3>📊 Podsumowanie</h3>
                <p><strong>Total:</strong> 320 zł</p>
                <p><strong>Deposit:</strong> 96 zł</p>
                <p><strong>Remaining:</strong> 224 zł</p>
            </div>
            
            <p><strong>Data testu:</strong> {DateTime.Now:dd.MM.yyyy HH:mm:ss}</p>
        </div>
        
        <div class='footer'>
            <p>SportRental - Test Email</p>
            <p style='font-size: 12px;'>Można bezpiecznie usunąć</p>
        </div>
    </div>
</body>
</html>";
    }
}
