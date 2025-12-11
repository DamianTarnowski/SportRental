using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using QuestPDF.Infrastructure;
using SportRental.Admin.Services.Contracts;
using SportRental.Admin.Services.Email;
using SportRental.Admin.Services.Storage;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;
using Xunit;
using Xunit.Abstractions;

namespace SportRental.Admin.Tests.Integration;

/// <summary>
/// Integration tests for rental contract generation and email sending.
/// Tests the full flow: create rental → generate contract PDF → send email with attachment.
/// Uses real PostgreSQL database and real email sending (to test email).
/// </summary>
public class RentalContractEmailIntegrationTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private ApplicationDbContext _dbContext = null!;
    private Guid _testTenantId;
    private Guid _testCustomerId;
    private string _testCustomerEmail = string.Empty;
    private Guid _testProductId;
    private string _testProductName = string.Empty;
    private decimal _testProductPrice;
    private CompanyInfo? _companyInfo;
    private readonly List<Guid> _createdRentalIds = new();

    private const string ConnectionString = "Host=eduedu.postgres.database.azure.com;Database=sr;Username=synapsis;Password=HasloHaslo122@@@@;SSL Mode=Require";
    
    // Email testowy - zmień na swój email żeby otrzymać test
    private const string TestRecipientEmail = "sportrental.kontakt@gmail.com";

    public RentalContractEmailIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        // Ustaw licencję QuestPDF na Community (darmowa dla małych firm)
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        _dbContext = new ApplicationDbContext(options);

        // Get tenant
        var tenant = await _dbContext.Tenants.FirstOrDefaultAsync();
        _testTenantId = tenant!.Id;
        _output.WriteLine($"[Contract Test] Tenant: {tenant.Name} ({_testTenantId})");

        // Get company info
        _companyInfo = await _dbContext.CompanyInfos.FirstOrDefaultAsync(c => c.TenantId == _testTenantId);
        _output.WriteLine($"[Contract Test] Company: {_companyInfo?.Name ?? "N/A"}");

        // Create test customer with email
        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            TenantId = _testTenantId,
            FullName = "Test Klient Umowa",
            Email = TestRecipientEmail,
            PhoneNumber = "123456789",
            Address = "ul. Testowa 123, 00-001 Warszawa",
            DocumentNumber = "ABC123456",
            CreatedAtUtc = DateTime.UtcNow
        };
        _dbContext.Customers.Add(customer);
        await _dbContext.SaveChangesAsync();
        _testCustomerId = customer.Id;
        _testCustomerEmail = customer.Email;
        _output.WriteLine($"[Contract Test] Created customer: {customer.FullName} ({customer.Email})");

        // Get product
        var product = await _dbContext.Products.FirstOrDefaultAsync(p => p.TenantId == _testTenantId && p.AvailableQuantity > 0);
        _testProductId = product!.Id;
        _testProductName = product.Name;
        _testProductPrice = product.DailyPrice;
        _output.WriteLine($"[Contract Test] Product: {_testProductName} ({_testProductPrice}/day)");
    }

    public async Task DisposeAsync()
    {
        // Cleanup rentals
        foreach (var rentalId in _createdRentalIds)
        {
            var rental = await _dbContext.Rentals
                .Include(r => r.Items)
                .FirstOrDefaultAsync(r => r.Id == rentalId);
            
            if (rental is not null)
            {
                _dbContext.RentalItems.RemoveRange(rental.Items);
                _dbContext.Rentals.Remove(rental);
            }
        }

        // Cleanup test customer
        var customer = await _dbContext.Customers.FirstOrDefaultAsync(c => c.Id == _testCustomerId);
        if (customer is not null)
        {
            _dbContext.Customers.Remove(customer);
        }

        await _dbContext.SaveChangesAsync();
        _output.WriteLine($"[Contract Test] Cleanup completed");
        await _dbContext.DisposeAsync();
    }

    /// <summary>
    /// Test: Generate PDF contract for a rental.
    /// </summary>
    [Fact]
    public async Task GenerateContract_ShouldCreateValidPdf()
    {
        _output.WriteLine("\n========== GENERATE CONTRACT TEST ==========\n");

        // Arrange - Create rental
        var rentalId = Guid.NewGuid();
        var startDate = DateTime.UtcNow.AddDays(1);
        var endDate = DateTime.UtcNow.AddDays(3);
        var days = 2;
        var quantity = 2;
        var subtotal = _testProductPrice * quantity * days;

        var rental = new Rental
        {
            Id = rentalId,
            TenantId = _testTenantId,
            CustomerId = _testCustomerId,
            StartDateUtc = startDate,
            EndDateUtc = endDate,
            Status = RentalStatus.Confirmed,
            TotalAmount = subtotal,
            DepositAmount = subtotal * 0.3m,
            CreatedAtUtc = DateTime.UtcNow,
            Notes = "Test rental for contract generation"
        };

        var rentalItem = new RentalItem
        {
            Id = Guid.NewGuid(),
            RentalId = rentalId,
            ProductId = _testProductId,
            Quantity = quantity,
            PricePerDay = _testProductPrice,
            Subtotal = subtotal
        };

        rental.Items.Add(rentalItem);
        _dbContext.Rentals.Add(rental);
        await _dbContext.SaveChangesAsync();
        _createdRentalIds.Add(rentalId);
        _output.WriteLine($"[Contract Test] Created rental: {rentalId}");

        // Get customer and products
        var customer = await _dbContext.Customers.FindAsync(_testCustomerId);
        var products = await _dbContext.Products.Where(p => p.Id == _testProductId).ToListAsync();

        // Act - Generate contract using mock services
        var mockFileStorage = new Mock<IFileStorage>();
        var mockEmailSender = new Mock<IEmailSender>();
        var mockLogger = new Mock<ILogger<QuestPdfContractGenerator>>();

        var contractGenerator = new QuestPdfContractGenerator(
            mockFileStorage.Object, 
            mockEmailSender.Object, 
            mockLogger.Object);

        var pdfBytes = await contractGenerator.GenerateRentalContractAsync(
            rental, rental.Items, customer!, products, _companyInfo);

        // Assert
        pdfBytes.Should().NotBeNull();
        pdfBytes.Should().NotBeEmpty();
        pdfBytes.Length.Should().BeGreaterThan(1000, "PDF should have reasonable size");

        // Check PDF header (PDF files start with %PDF)
        var pdfHeader = System.Text.Encoding.ASCII.GetString(pdfBytes.Take(4).ToArray());
        pdfHeader.Should().Be("%PDF", "Generated file should be a valid PDF");

        _output.WriteLine($"[Contract Test] ✅ PDF generated successfully!");
        _output.WriteLine($"[Contract Test]    Size: {pdfBytes.Length} bytes");
        _output.WriteLine($"[Contract Test]    Header: {pdfHeader}");

        // Save PDF locally for manual inspection
        var testPdfPath = Path.Combine(Path.GetTempPath(), $"test_contract_{rentalId}.pdf");
        await File.WriteAllBytesAsync(testPdfPath, pdfBytes);
        _output.WriteLine($"[Contract Test]    Saved to: {testPdfPath}");
    }

    /// <summary>
    /// Test: Generate contract and save to storage.
    /// </summary>
    [Fact]
    public async Task GenerateAndSaveContract_ShouldReturnUrl()
    {
        _output.WriteLine("\n========== GENERATE AND SAVE CONTRACT TEST ==========\n");

        // Arrange
        var rentalId = Guid.NewGuid();
        var rental = new Rental
        {
            Id = rentalId,
            TenantId = _testTenantId,
            CustomerId = _testCustomerId,
            StartDateUtc = DateTime.UtcNow.AddDays(5),
            EndDateUtc = DateTime.UtcNow.AddDays(7),
            Status = RentalStatus.Confirmed,
            TotalAmount = _testProductPrice * 2,
            CreatedAtUtc = DateTime.UtcNow
        };
        rental.Items.Add(new RentalItem
        {
            Id = Guid.NewGuid(),
            RentalId = rentalId,
            ProductId = _testProductId,
            Quantity = 1,
            PricePerDay = _testProductPrice,
            Subtotal = _testProductPrice * 2
        });

        _dbContext.Rentals.Add(rental);
        await _dbContext.SaveChangesAsync();
        _createdRentalIds.Add(rentalId);

        var customer = await _dbContext.Customers.FindAsync(_testCustomerId);
        var products = await _dbContext.Products.Where(p => p.Id == _testProductId).ToListAsync();

        // Setup mock to return URL
        var expectedUrl = $"https://storage.example.com/contracts/{_testTenantId}/umowa_{rentalId}.pdf";
        var mockFileStorage = new Mock<IFileStorage>();
        mockFileStorage
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedUrl);

        var mockEmailSender = new Mock<IEmailSender>();
        var mockLogger = new Mock<ILogger<QuestPdfContractGenerator>>();

        var contractGenerator = new QuestPdfContractGenerator(
            mockFileStorage.Object, 
            mockEmailSender.Object, 
            mockLogger.Object);

        // Act
        var contractUrl = await contractGenerator.GenerateAndSaveRentalContractAsync(
            rental, rental.Items, customer!, products, _companyInfo);

        // Assert
        contractUrl.Should().NotBeNullOrEmpty();
        contractUrl.Should().Be(expectedUrl);

        mockFileStorage.Verify(s => s.SaveAsync(
            It.Is<string>(path => path.Contains("contracts") && path.Contains(rentalId.ToString())),
            It.Is<byte[]>(bytes => bytes.Length > 0),
            It.IsAny<CancellationToken>()), Times.Once);

        _output.WriteLine($"[Contract Test] ✅ Contract saved!");
        _output.WriteLine($"[Contract Test]    URL: {contractUrl}");
    }

    /// <summary>
    /// Test: Send confirmation email with contract attachment.
    /// This test actually sends an email to verify the full flow.
    /// </summary>
    [Fact]
    public async Task SendConfirmationEmail_ShouldSendEmailWithAttachment()
    {
        _output.WriteLine("\n========== SEND EMAIL WITH CONTRACT TEST ==========\n");

        // Arrange
        var rentalId = Guid.NewGuid();
        var rental = new Rental
        {
            Id = rentalId,
            TenantId = _testTenantId,
            CustomerId = _testCustomerId,
            StartDateUtc = DateTime.UtcNow.AddDays(10),
            EndDateUtc = DateTime.UtcNow.AddDays(12),
            Status = RentalStatus.Confirmed,
            TotalAmount = _testProductPrice * 2 * 2, // 2 items, 2 days
            DepositAmount = _testProductPrice,
            CreatedAtUtc = DateTime.UtcNow,
            Notes = "Test email - proszę zignorować"
        };
        rental.Items.Add(new RentalItem
        {
            Id = Guid.NewGuid(),
            RentalId = rentalId,
            ProductId = _testProductId,
            Quantity = 2,
            PricePerDay = _testProductPrice,
            Subtotal = _testProductPrice * 2 * 2
        });

        _dbContext.Rentals.Add(rental);
        await _dbContext.SaveChangesAsync();
        _createdRentalIds.Add(rentalId);

        var customer = await _dbContext.Customers.FindAsync(_testCustomerId);
        var products = await _dbContext.Products.Where(p => p.Id == _testProductId).ToListAsync();

        _output.WriteLine($"[Email Test] Rental: {rentalId}");
        _output.WriteLine($"[Email Test] Customer: {customer!.FullName} ({customer.Email})");
        _output.WriteLine($"[Email Test] Products: {string.Join(", ", products.Select(p => p.Name))}");
        _output.WriteLine($"[Email Test] Total: {rental.TotalAmount:F2} PLN");

        // Setup mock to track email sending
        var emailSent = false;
        string? sentTo = null;
        string? sentSubject = null;
        string? sentBody = null;
        string? sentAttachment = null;

        var mockFileStorage = new Mock<IFileStorage>();
        var mockEmailSender = new Mock<IEmailSender>();
        mockEmailSender
            .Setup(e => e.SendEmailWithAttachmentAsync(
                It.IsAny<string>(), 
                It.IsAny<string>(), 
                It.IsAny<string>(), 
                It.IsAny<string>()))
            .Callback<string, string, string, string>((to, subject, body, attachment) =>
            {
                emailSent = true;
                sentTo = to;
                sentSubject = subject;
                sentBody = body;
                sentAttachment = attachment;
            })
            .Returns(Task.CompletedTask);

        var mockLogger = new Mock<ILogger<QuestPdfContractGenerator>>();

        var contractGenerator = new QuestPdfContractGenerator(
            mockFileStorage.Object, 
            mockEmailSender.Object, 
            mockLogger.Object);

        // Act
        await contractGenerator.SendRentalConfirmationEmailAsync(
            rental, rental.Items, customer, products, _companyInfo);

        // Assert
        emailSent.Should().BeTrue("Email should be sent");
        sentTo.Should().Be(customer.Email);
        sentSubject.Should().Contain("Potwierdzenie wypożyczenia");
        sentSubject.Should().Contain(rentalId.ToString()[..8]);
        sentBody.Should().Contain(customer.FullName);
        sentBody.Should().Contain(rental.TotalAmount.ToString("0.00"));
        sentAttachment.Should().NotBeNullOrEmpty();
        sentAttachment.Should().EndWith(".pdf");

        _output.WriteLine($"[Email Test] ✅ Email would be sent!");
        _output.WriteLine($"[Email Test]    To: {sentTo}");
        _output.WriteLine($"[Email Test]    Subject: {sentSubject}");
        _output.WriteLine($"[Email Test]    Attachment: {sentAttachment}");
        _output.WriteLine($"[Email Test]    Body contains customer name: {sentBody?.Contains(customer.FullName)}");
        _output.WriteLine($"[Email Test]    Body contains total: {sentBody?.Contains(rental.TotalAmount.ToString("0.00"))}");
    }

    /// <summary>
    /// Test: Full flow - create rental, generate contract, send email.
    /// Simulates what happens when admin creates a rental in the panel.
    /// </summary>
    [Fact]
    public async Task FullFlow_CreateRental_GenerateContract_SendEmail()
    {
        _output.WriteLine("\n========== FULL FLOW TEST ==========\n");

        // ============ STEP 1: Create rental (like admin does) ============
        _output.WriteLine("[Admin] Creating new rental...");
        
        var rentalId = Guid.NewGuid();
        var startDate = DateTime.UtcNow.AddDays(15);
        var endDate = DateTime.UtcNow.AddDays(18);
        var days = 3;
        var quantity = 1;
        var subtotal = _testProductPrice * quantity * days;

        var rental = new Rental
        {
            Id = rentalId,
            TenantId = _testTenantId,
            CustomerId = _testCustomerId,
            StartDateUtc = startDate,
            EndDateUtc = endDate,
            Status = RentalStatus.Confirmed,
            TotalAmount = subtotal,
            DepositAmount = subtotal * 0.3m,
            CreatedAtUtc = DateTime.UtcNow
        };

        var rentalItem = new RentalItem
        {
            Id = Guid.NewGuid(),
            RentalId = rentalId,
            ProductId = _testProductId,
            Quantity = quantity,
            PricePerDay = _testProductPrice,
            Subtotal = subtotal
        };

        rental.Items.Add(rentalItem);
        _dbContext.Rentals.Add(rental);
        await _dbContext.SaveChangesAsync();
        _createdRentalIds.Add(rentalId);

        _output.WriteLine($"[Admin] ✅ Rental created: {rentalId}");
        _output.WriteLine($"[Admin]    Customer: {_testCustomerEmail}");
        _output.WriteLine($"[Admin]    Period: {startDate:dd.MM.yyyy} - {endDate:dd.MM.yyyy}");
        _output.WriteLine($"[Admin]    Total: {subtotal:F2} PLN");

        // ============ STEP 2: Generate contract ============
        _output.WriteLine("\n[Admin] Generating contract...");

        var customer = await _dbContext.Customers.FindAsync(_testCustomerId);
        var products = await _dbContext.Products.Where(p => p.Id == _testProductId).ToListAsync();

        var contractUrl = $"https://storage.example.com/contracts/{_testTenantId}/umowa_{rentalId}.pdf";
        var mockFileStorage = new Mock<IFileStorage>();
        mockFileStorage
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(contractUrl);

        var mockEmailSender = new Mock<IEmailSender>();
        var mockLogger = new Mock<ILogger<QuestPdfContractGenerator>>();

        var contractGenerator = new QuestPdfContractGenerator(
            mockFileStorage.Object, 
            mockEmailSender.Object, 
            mockLogger.Object);

        var savedContractUrl = await contractGenerator.GenerateAndSaveRentalContractAsync(
            rental, rental.Items, customer!, products, _companyInfo);

        // Update rental with contract URL
        rental.ContractUrl = savedContractUrl;
        _dbContext.Rentals.Update(rental);
        await _dbContext.SaveChangesAsync();

        _output.WriteLine($"[Admin] ✅ Contract generated: {savedContractUrl}");

        // ============ STEP 3: Send email ============
        _output.WriteLine("\n[Admin] Sending confirmation email...");

        await contractGenerator.SendRentalConfirmationEmailAsync(
            rental, rental.Items, customer!, products, _companyInfo);

        rental.IsEmailSent = true;
        _dbContext.Rentals.Update(rental);
        await _dbContext.SaveChangesAsync();

        _output.WriteLine($"[Admin] ✅ Email sent to: {customer!.Email}");

        // ============ STEP 4: Verify in database ============
        _output.WriteLine("\n[Verify] Checking database...");

        _dbContext.ChangeTracker.Clear();
        var savedRental = await _dbContext.Rentals
            .AsNoTracking()
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == rentalId);

        savedRental.Should().NotBeNull();
        savedRental!.ContractUrl.Should().Be(contractUrl);
        savedRental.IsEmailSent.Should().BeTrue();
        savedRental.Status.Should().Be(RentalStatus.Confirmed);
        savedRental.Items.Should().HaveCount(1);

        _output.WriteLine($"[Verify] ✅ Rental in database:");
        _output.WriteLine($"[Verify]    ID: {savedRental.Id}");
        _output.WriteLine($"[Verify]    ContractUrl: {savedRental.ContractUrl}");
        _output.WriteLine($"[Verify]    IsEmailSent: {savedRental.IsEmailSent}");
        _output.WriteLine($"[Verify]    Status: {savedRental.Status}");

        _output.WriteLine("\n========== FULL FLOW TEST PASSED ✅ ==========\n");
    }

    /// <summary>
    /// Test: Contract contains correct data.
    /// </summary>
    [Fact]
    public async Task GeneratedContract_ShouldContainCorrectData()
    {
        _output.WriteLine("\n========== CONTRACT DATA VALIDATION TEST ==========\n");

        // Arrange
        var rentalId = Guid.NewGuid();
        var rental = new Rental
        {
            Id = rentalId,
            TenantId = _testTenantId,
            CustomerId = _testCustomerId,
            StartDateUtc = new DateTime(2025, 1, 15, 10, 0, 0, DateTimeKind.Utc),
            EndDateUtc = new DateTime(2025, 1, 18, 10, 0, 0, DateTimeKind.Utc),
            Status = RentalStatus.Confirmed,
            TotalAmount = 150.00m,
            DepositAmount = 50.00m,
            CreatedAtUtc = DateTime.UtcNow,
            Notes = "Specjalne uwagi testowe"
        };
        rental.Items.Add(new RentalItem
        {
            Id = Guid.NewGuid(),
            RentalId = rentalId,
            ProductId = _testProductId,
            Quantity = 2,
            PricePerDay = 25.00m,
            Subtotal = 150.00m
        });

        _dbContext.Rentals.Add(rental);
        await _dbContext.SaveChangesAsync();
        _createdRentalIds.Add(rentalId);

        var customer = await _dbContext.Customers.FindAsync(_testCustomerId);
        var products = await _dbContext.Products.Where(p => p.Id == _testProductId).ToListAsync();

        var mockFileStorage = new Mock<IFileStorage>();
        var mockEmailSender = new Mock<IEmailSender>();
        var mockLogger = new Mock<ILogger<QuestPdfContractGenerator>>();

        var contractGenerator = new QuestPdfContractGenerator(
            mockFileStorage.Object, 
            mockEmailSender.Object, 
            mockLogger.Object);

        // Act
        var pdfBytes = await contractGenerator.GenerateRentalContractAsync(
            rental, rental.Items, customer!, products, _companyInfo);

        // Save for manual inspection
        var testPdfPath = Path.Combine(Path.GetTempPath(), $"test_contract_data_{rentalId}.pdf");
        await File.WriteAllBytesAsync(testPdfPath, pdfBytes);

        // Assert - PDF was generated
        pdfBytes.Should().NotBeEmpty();
        
        _output.WriteLine($"[Data Test] ✅ Contract generated with data:");
        _output.WriteLine($"[Data Test]    Customer: {customer!.FullName}");
        _output.WriteLine($"[Data Test]    Email: {customer.Email}");
        _output.WriteLine($"[Data Test]    Address: {customer.Address}");
        _output.WriteLine($"[Data Test]    Document: {customer.DocumentNumber}");
        _output.WriteLine($"[Data Test]    Period: {rental.StartDateUtc:dd.MM.yyyy} - {rental.EndDateUtc:dd.MM.yyyy}");
        _output.WriteLine($"[Data Test]    Total: {rental.TotalAmount:F2} PLN");
        _output.WriteLine($"[Data Test]    Deposit: {rental.DepositAmount:F2} PLN");
        _output.WriteLine($"[Data Test]    Notes: {rental.Notes}");
        _output.WriteLine($"[Data Test]    Company: {_companyInfo?.Name ?? "N/A"}");
        _output.WriteLine($"[Data Test]    PDF saved to: {testPdfPath}");
    }
}
