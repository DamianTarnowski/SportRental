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
/// Integration tests for rental confirmation emails with PDF attachments using real Onet SMTP
/// </summary>
public class RentalConfirmationEmailIntegrationTests
{
    private readonly ITestOutputHelper _output;
    private readonly IConfiguration _configuration;

    public RentalConfirmationEmailIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        
        _configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.Development.json", optional: false)
            .Build();
    }

    [Fact]
    public async Task SendRentalConfirmation_WithPdfAttachment_ToOnetEmail_Succeeds()
    {
        // Arrange
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
        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            FullName = "Jan Kowalski",
            Email = _configuration["TestAccounts:TestCustomer:Email"] ?? "testklient@op.pl",
            PhoneNumber = "+48 123 456 789",
            DocumentNumber = "ABC123456"
        };

        var rental = new Rental
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CustomerId = customer.Id,
            Customer = customer,
            StartDateUtc = DateTime.UtcNow.AddDays(1),
            EndDateUtc = DateTime.UtcNow.AddDays(4),
            TotalAmount = 720m,
            DepositAmount = 216m,
            Status = RentalStatus.Confirmed,
            PaymentStatus = "Succeeded"
        };

        var items = new List<(Product product, int quantity)>
        {
            (new Product
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Name = "Narty Rossignol Hero Elite",
                DailyPrice = 120m,
                Description = "Profesjonalne narty zjazdowe"
            }, 2)
        };

        // Act
        _output.WriteLine($"📧 Sending rental confirmation to {customer.Email}...");
        _output.WriteLine($"   Rental ID: {rental.Id}");
        _output.WriteLine($"   Total: {rental.TotalAmount} PLN");
        _output.WriteLine($"   Deposit: {rental.DepositAmount} PLN");

        var exception = await Record.ExceptionAsync(async () =>
        {
            await confirmationService.SendRentalConfirmationAsync(
                customer.Email,
                customer.FullName,
                customer,
                rental,
                items);
        });

        // Assert
        exception.Should().BeNull("Email with PDF should be sent successfully");
        _output.WriteLine("✅ Email with PDF contract sent successfully!");
        _output.WriteLine($"   Check inbox: {customer.Email}");
        _output.WriteLine("   Expected: HTML email + PDF attachment");
    }

    [Fact(Skip = "Integration test - manual only")]
    public async Task SendRentalConfirmation_MultipleProducts_Succeeds()
    {
        // Arrange
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

        var tenantId = Guid.NewGuid();
        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            FullName = "Anna Nowak",
            Email = _configuration["TestAccounts:TestCustomer:Email"] ?? "testklient@op.pl",
            PhoneNumber = "+48 987 654 321",
            DocumentNumber = "XYZ789012"
        };

        var rental = new Rental
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CustomerId = customer.Id,
            Customer = customer,
            StartDateUtc = DateTime.UtcNow.AddDays(2),
            EndDateUtc = DateTime.UtcNow.AddDays(7),
            TotalAmount = 2400m,
            DepositAmount = 720m,
            Status = RentalStatus.Confirmed,
            PaymentStatus = "Succeeded"
        };

        var items = new List<(Product product, int quantity)>
        {
            (new Product { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Narty Atomic", DailyPrice = 120m }, 2),
            (new Product { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Buty narciarskie", DailyPrice = 40m }, 2),
            (new Product { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Kask", DailyPrice = 20m }, 2)
        };

        // Act
        _output.WriteLine($"📧 Sending confirmation with multiple products...");
        _output.WriteLine($"   Customer: {customer.FullName}");
        _output.WriteLine($"   Products: {items.Count}");
        _output.WriteLine($"   Duration: {(rental.EndDateUtc - rental.StartDateUtc).Days} days");

        var exception = await Record.ExceptionAsync(async () =>
        {
            await confirmationService.SendRentalConfirmationAsync(
                customer.Email,
                customer.FullName,
                customer,
                rental,
                items);
        });

        // Assert
        exception.Should().BeNull();
        _output.WriteLine("✅ Multi-product confirmation sent!");
    }

    [Fact(Skip = "Integration test - manual only")]
    public async Task SendRentalConfirmation_LongRentalPeriod_Succeeds()
    {
        // Arrange
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

        var tenantId = Guid.NewGuid();
        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            FullName = "Piotr Wiśniewski",
            Email = _configuration["TestAccounts:RentalOwner:Email"] ?? "contact.sportrental@op.pl",
            PhoneNumber = "+48 555 111 222",
            DocumentNumber = "DEF456789"
        };

        var rental = new Rental
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CustomerId = customer.Id,
            Customer = customer,
            StartDateUtc = DateTime.UtcNow.AddDays(1),
            EndDateUtc = DateTime.UtcNow.AddDays(15), // 14 days
            TotalAmount = 3360m,
            DepositAmount = 1008m,
            Status = RentalStatus.Confirmed,
            PaymentStatus = "Succeeded"
        };

        var items = new List<(Product product, int quantity)>
        {
            (new Product { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Rower górski Trek", DailyPrice = 120m }, 2)
        };

        // Act
        _output.WriteLine($"📧 Testing long rental period (14 days)...");

        var exception = await Record.ExceptionAsync(async () =>
        {
            await confirmationService.SendRentalConfirmationAsync(
                customer.Email,
                customer.FullName,
                customer,
                rental,
                items);
        });

        // Assert
        exception.Should().BeNull();
        _output.WriteLine("✅ Long rental confirmation sent!");
        _output.WriteLine($"   Total amount: {rental.TotalAmount} PLN for 14 days");
    }
}
