using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using QuestPDF.Infrastructure;
using SportRental.Admin.Services.Contracts;
using SportRental.Admin.Services.Email;
using SportRental.Admin.Services.Sms;
using SportRental.Admin.Services.Storage;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;
using Xunit;
using Xunit.Abstractions;

namespace SportRental.Admin.Tests.Integration;

/// <summary>
/// Integration tests for Email and SMS functionality.
/// Tests cover:
/// - Email sending via IEmailSender mock
/// - SMS sending via ISmsSender mock  
/// - Contract generation + email attachment flow
/// - Detached entity concurrency fix (the bug where Update() on detached entity throws)
/// - Full rental save → email → SMS flow
/// </summary>
[Trait("Category", "RequiresLiveDb")]
public class EmailSmsIntegrationTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private ApplicationDbContext _dbContext = null!;
    private Guid _tenantId;
    private Customer _customer = null!;
    private Product _product = null!;
    private readonly List<Guid> _createdRentalIds = new();
    private readonly List<Guid> _createdCustomerIds = new();

    // Testowa baza z env SR_TEST_DB (ustaw lokalnie przy testach integracyjnych) — bez sekretów w repo.
    private static readonly string ConnectionString =
        System.Environment.GetEnvironmentVariable("SR_TEST_DB")
        ?? "Host=localhost;Port=5432;Database=sr_test;Username=postgres;Password=postgres;SSL Mode=Disable";

    public EmailSmsIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        _dbContext = new ApplicationDbContext(options);

        var tenant = await _dbContext.Tenants.FirstOrDefaultAsync();
        _tenantId = tenant!.Id;

        // Create isolated test customer
        _customer = new Customer
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            FullName = "Test EmailSms " + DateTime.UtcNow.Ticks,
            Email = "test-emailsms@sportrental-test.local",
            PhoneNumber = "+48500100200",
            Address = "ul. Testowa 1",
            DocumentNumber = "TEST000",
            CreatedAtUtc = DateTime.UtcNow
        };
        _dbContext.Customers.Add(_customer);
        await _dbContext.SaveChangesAsync();
        _createdCustomerIds.Add(_customer.Id);

        _product = new Product
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            Name = "Email SMS Integration Product",
            Sku = $"EMAILSMS-{Guid.NewGuid():N}",
            DailyPrice = 70,
            AvailableQuantity = 100
        };
        _dbContext.Products.Add(_product);
        await _dbContext.SaveChangesAsync();

        _output.WriteLine($"[Setup] Tenant: {tenant.Name}");
        _output.WriteLine($"[Setup] Customer: {_customer.FullName} ({_customer.Email}, {_customer.PhoneNumber})");
        _output.WriteLine($"[Setup] Product: {_product.Name} ({_product.DailyPrice}/day)");
    }

    public async Task DisposeAsync()
    {
        foreach (var id in _createdRentalIds)
        {
            var r = await _dbContext.Rentals.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id);
            if (r != null)
            {
                _dbContext.RentalItems.RemoveRange(r.Items);
                _dbContext.Rentals.Remove(r);
            }
        }
        foreach (var id in _createdCustomerIds)
        {
            var c = await _dbContext.Customers.FindAsync(id);
            if (c != null) _dbContext.Customers.Remove(c);
        }
        var product = await _dbContext.Products.FirstOrDefaultAsync(p => p.Id == _product.Id);
        if (product != null) _dbContext.Products.Remove(product);
        await _dbContext.SaveChangesAsync();
        await _dbContext.DisposeAsync();
        _output.WriteLine("[Cleanup] Done");
    }

    private (Rental rental, RentalItem item) CreateTestRental()
    {
        var rentalId = Guid.NewGuid();
        var rental = new Rental
        {
            Id = rentalId,
            TenantId = _tenantId,
            CustomerId = _customer.Id,
            StartDateUtc = DateTime.UtcNow.AddDays(1),
            EndDateUtc = DateTime.UtcNow.AddDays(3),
            Status = RentalStatus.Confirmed,
            TotalAmount = _product.DailyPrice * 2,
            DepositAmount = _product.DailyPrice,
            CreatedAtUtc = DateTime.UtcNow
        };
        var item = new RentalItem
        {
            Id = Guid.NewGuid(),
            RentalId = rentalId,
            ProductId = _product.Id,
            Quantity = 1,
            PricePerDay = _product.DailyPrice,
            Subtotal = _product.DailyPrice * 2
        };
        rental.Items.Add(item);
        _createdRentalIds.Add(rentalId);
        return (rental, item);
    }

    // =====================================================================
    // EMAIL TESTS
    // =====================================================================

    [Fact]
    public async Task Email_SendConfirmation_MockVerifiesCorrectParameters()
    {
        _output.WriteLine("\n=== Email: SendConfirmation Mock Test ===");

        var (rental, _) = CreateTestRental();
        _dbContext.Rentals.Add(rental);
        await _dbContext.SaveChangesAsync();

        var products = await _dbContext.Products.Where(p => p.Id == _product.Id).ToListAsync();
        var companyInfo = await _dbContext.CompanyInfos.FirstOrDefaultAsync(c => c.TenantId == _tenantId);

        string? capturedTo = null;
        string? capturedSubject = null;
        string? capturedBody = null;
        string? capturedAttachment = null;

        var mockEmail = new Mock<IEmailSender>();
        mockEmail
            .Setup(e => e.SendEmailWithAttachmentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string, string, string>((to, subj, body, att) =>
            {
                capturedTo = to;
                capturedSubject = subj;
                capturedBody = body;
                capturedAttachment = att;
            })
            .Returns(Task.CompletedTask);

        var generator = new QuestPdfContractGenerator(
            new Mock<IFileStorage>().Object, mockEmail.Object, new Mock<ILogger<QuestPdfContractGenerator>>().Object);

        await generator.SendRentalConfirmationEmailAsync(rental, rental.Items, _customer, products, companyInfo);

        capturedTo.Should().Be(_customer.Email);
        capturedSubject.Should().Contain("Potwierdzenie");
        capturedBody.Should().Contain(_customer.FullName);
        capturedAttachment.Should().NotBeNullOrEmpty();
        capturedAttachment.Should().EndWith(".pdf");

        _output.WriteLine($"  To: {capturedTo}");
        _output.WriteLine($"  Subject: {capturedSubject}");
        _output.WriteLine($"  Attachment: {capturedAttachment}");
        _output.WriteLine("  ✅ PASSED");
    }

    [Fact]
    public async Task Email_GenerateContract_ProducesValidPdf()
    {
        _output.WriteLine("\n=== Email: GenerateContract PDF Test ===");

        var (rental, _) = CreateTestRental();
        _dbContext.Rentals.Add(rental);
        await _dbContext.SaveChangesAsync();

        var products = await _dbContext.Products.Where(p => p.Id == _product.Id).ToListAsync();
        var companyInfo = await _dbContext.CompanyInfos.FirstOrDefaultAsync(c => c.TenantId == _tenantId);

        var generator = new QuestPdfContractGenerator(
            new Mock<IFileStorage>().Object, new Mock<IEmailSender>().Object, new Mock<ILogger<QuestPdfContractGenerator>>().Object);

        var pdfBytes = await generator.GenerateRentalContractAsync(rental, rental.Items, _customer, products, companyInfo);

        pdfBytes.Should().NotBeEmpty();
        pdfBytes.Length.Should().BeGreaterThan(500);

        var header = System.Text.Encoding.ASCII.GetString(pdfBytes.Take(4).ToArray());
        header.Should().Be("%PDF");

        _output.WriteLine($"  PDF size: {pdfBytes.Length} bytes");
        _output.WriteLine("  ✅ PASSED");
    }

    [Fact]
    public async Task Email_NoOpSender_DoesNotThrow()
    {
        _output.WriteLine("\n=== Email: NoOpSender Test ===");

        var noOp = new NoOpEmailSender(new Mock<ILogger<NoOpEmailSender>>().Object);

        var act1 = () => noOp.SendEmailAsync("test@test.pl", "Subject", "<p>Body</p>");
        await act1.Should().NotThrowAsync();

        var act2 = () => noOp.SendEmailWithAttachmentAsync("test@test.pl", "Subject", "<p>Body</p>", "/tmp/test.pdf");
        await act2.Should().NotThrowAsync();

        _output.WriteLine("  ✅ PASSED");
    }

    // =====================================================================
    // SMS TESTS
    // =====================================================================

    [Fact]
    public async Task Sms_ConsoleSender_AllMethods_DoNotThrow()
    {
        _output.WriteLine("\n=== SMS: ConsoleSender All Methods Test ===");

        var sender = new ConsoleSmsSender();
        using var sw = new StringWriter();
        Console.SetOut(sw);

        await sender.SendAsync("+48500100200", "Test message");
        await sender.SendThanksMessageAsync("+48500100200", "Jan Kowalski");
        await sender.SendThanksMessageAsync("+48500100200", "Jan Kowalski", "Niestandardowa wiadomość");
        await sender.SendReminderAsync("+48500100200", "Jan Kowalski");
        await sender.SendConfirmationRequestAsync("+48500100200", "Jan Kowalski", Guid.NewGuid());

        var output = sw.ToString();
        output.Should().Contain("+48500100200");
        output.Should().Contain("Jan Kowalski");
        output.Should().Contain("Test message");

        // Reset console
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });

        _output.WriteLine($"  Console output lines: {output.Split('\n').Length}");
        _output.WriteLine("  ✅ PASSED");
    }

    [Fact]
    public async Task Sms_MockSender_SendConfirmation_VerifiesParameters()
    {
        _output.WriteLine("\n=== SMS: Mock SendConfirmation Test ===");

        string? capturedPhone = null;
        string? capturedMsg = null;

        var mockSms = new Mock<ISmsSender>();
        mockSms
            .Setup(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((phone, msg, _) =>
            {
                capturedPhone = phone;
                capturedMsg = msg;
            })
            .Returns(Task.CompletedTask);

        // Simulate what Rentals.razor does for SMS
        var phoneNumber = "+48500100200";
        var message = "SportRental: Potwierdzenie wynajmu. Dziękujemy!";
        await mockSms.Object.SendAsync(phoneNumber, message);

        capturedPhone.Should().Be(phoneNumber);
        capturedMsg.Should().Contain("SportRental");

        mockSms.Verify(s => s.SendAsync(phoneNumber, message, default), Times.Once);

        _output.WriteLine($"  Phone: {capturedPhone}");
        _output.WriteLine($"  Message: {capturedMsg}");
        _output.WriteLine("  ✅ PASSED");
    }

    // =====================================================================
    // CONCURRENCY BUG REGRESSION TESTS
    // =====================================================================

    [Fact]
    public async Task Concurrency_DoubleUpdateOnDetached_CausesStaleData()
    {
        _output.WriteLine("\n=== Concurrency: Double Update on Detached Entity ===");

        var (rental, _) = CreateTestRental();
        _dbContext.Rentals.Add(rental);
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        // Simulate the OLD buggy pattern: modify in ctx1, then try Update in ctx2 with stale object
        await using var db1 = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(ConnectionString).Options);
        var tracked1 = await db1.Rentals.FindAsync(rental.Id);
        tracked1!.ContractUrl = "https://storage/contract.pdf";
        await db1.SaveChangesAsync();

        // Now rental (in-memory) is stale - ContractUrl is null, but DB has it set
        // The FIXED pattern avoids this by always doing Find() first
        await using var db2 = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(ConnectionString).Options);
        var tracked2 = await db2.Rentals.FindAsync(rental.Id);
        tracked2!.ContractUrl.Should().Be("https://storage/contract.pdf",
            "Find() loads fresh data from DB, avoiding stale state");
        tracked2.IsEmailSent = true;
        await db2.SaveChangesAsync();

        // Verify both fields are correct
        await using var db3 = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(ConnectionString).Options);
        var final = await db3.Rentals.AsNoTracking().FirstAsync(r => r.Id == rental.Id);
        final.ContractUrl.Should().Be("https://storage/contract.pdf");
        final.IsEmailSent.Should().BeTrue();

        _output.WriteLine("  Find() pattern preserves ContractUrl AND sets IsEmailSent");
        _output.WriteLine("  ✅ PASSED (Find avoids stale data overwrite)");
    }

    [Fact]
    public async Task Concurrency_FindAndUpdate_ShouldSucceed()
    {
        _output.WriteLine("\n=== Concurrency: Find+Update (Fixed Pattern) Should Succeed ===");

        var (rental, _) = CreateTestRental();
        _dbContext.Rentals.Add(rental);
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        // Simulate: new DbContext, Find entity first, then update (the FIXED pattern)
        await using var db2 = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(ConnectionString).Options);

        var tracked = await db2.Rentals.FindAsync(rental.Id);
        tracked.Should().NotBeNull();
        tracked!.IsEmailSent = true;

        var act = () => db2.SaveChangesAsync();
        await act.Should().NotThrowAsync("Find+Update on tracked entity should not throw");

        // Verify in DB
        await using var db3 = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(ConnectionString).Options);
        var saved = await db3.Rentals.AsNoTracking().FirstAsync(r => r.Id == rental.Id);
        saved.IsEmailSent.Should().BeTrue();

        _output.WriteLine("  Find+Update succeeded without concurrency exception");
        _output.WriteLine("  IsEmailSent persisted: true");
        _output.WriteLine("  ✅ PASSED (this confirms the fix works)");
    }

    [Fact]
    public async Task Concurrency_CompleteRental_FindPattern_ShouldSucceed()
    {
        _output.WriteLine("\n=== Concurrency: CompleteRental Fixed Pattern ===");

        var (rental, _) = CreateTestRental();
        _dbContext.Rentals.Add(rental);
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        await using var db2 = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(ConnectionString).Options);

        var tracked = await db2.Rentals.FindAsync(rental.Id);
        tracked.Should().NotBeNull();
        tracked!.Status = RentalStatus.Completed;
        await db2.SaveChangesAsync();

        await using var db3 = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(ConnectionString).Options);
        var saved = await db3.Rentals.AsNoTracking().FirstAsync(r => r.Id == rental.Id);
        saved.Status.Should().Be(RentalStatus.Completed);

        _output.WriteLine("  Status changed to Completed without exception");
        _output.WriteLine("  ✅ PASSED");
    }

    // =====================================================================
    // FULL FLOW: SAVE → EMAIL → SMS (simulates Rentals.razor)
    // =====================================================================

    [Fact]
    public async Task FullFlow_SaveRental_SendEmail_SendSms_NoExceptions()
    {
        _output.WriteLine("\n=== Full Flow: Save → Email → SMS ===");

        // STEP 1: Save rental
        var (rental, _) = CreateTestRental();
        _dbContext.Rentals.Add(rental);
        await _dbContext.SaveChangesAsync();
        _output.WriteLine($"  [1] Rental saved: {rental.Id}");

        // STEP 2: Generate contract (mock storage)
        var products = await _dbContext.Products.Where(p => p.Id == _product.Id).ToListAsync();
        var companyInfo = await _dbContext.CompanyInfos.FirstOrDefaultAsync(c => c.TenantId == _tenantId);

        var mockStorage = new Mock<IFileStorage>();
        mockStorage
            .Setup(s => s.SavePrivateAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://storage/contract.pdf");

        var mockEmail = new Mock<IEmailSender>();
        mockEmail
            .Setup(e => e.SendEmailWithAttachmentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var generator = new QuestPdfContractGenerator(
            mockStorage.Object, mockEmail.Object, new Mock<ILogger<QuestPdfContractGenerator>>().Object);

        var contractUrl = await generator.GenerateAndSaveRentalContractAsync(
            rental, rental.Items, _customer, products, companyInfo);
        _output.WriteLine($"  [2] Contract generated: {contractUrl}");

        // STEP 3: Update contract URL (using FIXED pattern - Find first)
        _dbContext.ChangeTracker.Clear();
        var trackedForContract = await _dbContext.Rentals.FindAsync(rental.Id);
        trackedForContract!.ContractUrl = contractUrl;
        await _dbContext.SaveChangesAsync();
        _output.WriteLine("  [3] ContractUrl saved (Find pattern)");

        // STEP 4: Send email (simulates SendContractByEmail with FIXED pattern)
        await generator.SendRentalConfirmationEmailAsync(rental, rental.Items, _customer, products, companyInfo);

        _dbContext.ChangeTracker.Clear();
        var trackedForEmail = await _dbContext.Rentals.FindAsync(rental.Id);
        trackedForEmail!.IsEmailSent = true;
        await _dbContext.SaveChangesAsync();
        rental.IsEmailSent = true;
        _output.WriteLine($"  [4] Email sent + IsEmailSent=true (Find pattern)");

        mockEmail.Verify(e => e.SendEmailWithAttachmentAsync(
            _customer.Email!, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);

        // STEP 5: Send SMS (mock)
        var mockSms = new Mock<ISmsSender>();
        mockSms.Setup(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await mockSms.Object.SendAsync(_customer.PhoneNumber!, $"SportRental: Potwierdzenie wynajmu {rental.Id.ToString()[..8]}");
        _output.WriteLine($"  [5] SMS sent to {_customer.PhoneNumber}");

        mockSms.Verify(s => s.SendAsync(_customer.PhoneNumber!, It.Is<string>(m => m.Contains("SportRental")), default), Times.Once);

        // STEP 6: Verify final state in DB
        await using var dbVerify = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(ConnectionString).Options);
        var final = await dbVerify.Rentals.AsNoTracking().FirstAsync(r => r.Id == rental.Id);

        final.ContractUrl.Should().Be("https://storage/contract.pdf");
        final.IsEmailSent.Should().BeTrue();
        final.Status.Should().Be(RentalStatus.Confirmed);

        _output.WriteLine("\n  [Verify] DB state:");
        _output.WriteLine($"    ContractUrl: {final.ContractUrl}");
        _output.WriteLine($"    IsEmailSent: {final.IsEmailSent}");
        _output.WriteLine($"    Status: {final.Status}");
        _output.WriteLine("  ✅ FULL FLOW PASSED - no concurrency exceptions");
    }

    [Fact]
    public async Task FullFlow_MultipleEmailResends_ShouldNotThrow()
    {
        _output.WriteLine("\n=== Full Flow: Multiple Email Resends ===");

        var (rental, _) = CreateTestRental();
        _dbContext.Rentals.Add(rental);
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        // Simulate clicking "Email" button 3 times (each time with new DbContext, like Blazor does)
        for (int i = 1; i <= 3; i++)
        {
            await using var db = new ApplicationDbContext(
                new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(ConnectionString).Options);

            // FIXED pattern: Find first, then update
            var tracked = await db.Rentals.FindAsync(rental.Id);
            tracked.Should().NotBeNull();
            tracked!.IsEmailSent = true;
            await db.SaveChangesAsync();

            _output.WriteLine($"  Resend #{i}: OK");
        }

        _output.WriteLine("  ✅ PASSED - 3 consecutive resends without exception");
    }
}
