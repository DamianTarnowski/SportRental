using FluentAssertions;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Search;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MimeKit;
using Moq;
using QuestPDF.Infrastructure;
using SportRental.Admin.Services.Contracts;
using SportRental.Admin.Services.Email;
using SportRental.Admin.Services.Storage;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace SportRental.Admin.Tests.Integration;

/// <summary>
/// End-to-End integration test for rental contract generation and email delivery.
/// 
/// This test performs the FULL flow:
/// 1. Creates a rental in the REAL PostgreSQL database
/// 2. Generates a PDF contract using QuestPDF
/// 3. Sends a REAL email via Gmail SMTP
/// 4. Connects to Gmail via IMAP and retrieves the email
/// 5. Verifies email content and PDF attachment
/// 
/// ⚠️ This test sends REAL emails - use sparingly!
/// </summary>
public class RentalContractEmailE2ETests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private ApplicationDbContext _dbContext = null!;
    private Guid _testTenantId;
    private Guid _testCustomerId;
    private Guid _testProductId;
    private string _testProductName = string.Empty;
    private decimal _testProductPrice;
    private CompanyInfo? _companyInfo;
    private readonly List<Guid> _createdRentalIds = new();

    // Database connection
    private const string ConnectionString = "Host=eduedu.postgres.database.azure.com;Database=sr;Username=synapsis;Password=HasloHaslo122@@@@;SSL Mode=Require";
    
    // Gmail configuration
    private const string GmailEmail = "sportrental.kontakt@gmail.com";
    private const string GmailAppPassword = "ujkp ivhx mdia uytm";
    private const string SmtpHost = "smtp.gmail.com";
    private const int SmtpPort = 587;
    private const string ImapHost = "imap.gmail.com";
    private const int ImapPort = 993;

    public RentalContractEmailE2ETests(ITestOutputHelper output)
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

        // Get tenant
        var tenant = await _dbContext.Tenants.FirstOrDefaultAsync();
        _testTenantId = tenant!.Id;
        _output.WriteLine($"[Setup] Tenant: {tenant.Name}");

        // Get company info
        _companyInfo = await _dbContext.CompanyInfos.FirstOrDefaultAsync(c => c.TenantId == _testTenantId);
        _output.WriteLine($"[Setup] Company: {_companyInfo?.Name ?? "N/A"}");

        // Create test customer with our Gmail
        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            TenantId = _testTenantId,
            FullName = "Test E2E Klient",
            Email = GmailEmail,
            PhoneNumber = "123456789",
            Address = "ul. Testowa E2E 123, 00-001 Warszawa",
            DocumentNumber = "E2E123456",
            CreatedAtUtc = DateTime.UtcNow
        };
        _dbContext.Customers.Add(customer);
        await _dbContext.SaveChangesAsync();
        _testCustomerId = customer.Id;
        _output.WriteLine($"[Setup] Customer: {customer.FullName} ({customer.Email})");

        // Get product
        var product = await _dbContext.Products.FirstOrDefaultAsync(p => p.TenantId == _testTenantId && p.AvailableQuantity > 0);
        _testProductId = product!.Id;
        _testProductName = product.Name;
        _testProductPrice = product.DailyPrice;
        _output.WriteLine($"[Setup] Product: {_testProductName} ({_testProductPrice}/day)");
    }

    public async Task DisposeAsync()
    {
        // Clear change tracker to avoid conflicts
        _dbContext.ChangeTracker.Clear();
        
        // Cleanup rentals using ExecuteDelete to avoid tracking issues
        foreach (var rentalId in _createdRentalIds)
        {
            await _dbContext.RentalItems.Where(i => i.RentalId == rentalId).ExecuteDeleteAsync();
            await _dbContext.Rentals.Where(r => r.Id == rentalId).ExecuteDeleteAsync();
        }

        // Cleanup test customer
        await _dbContext.Customers.Where(c => c.Id == _testCustomerId).ExecuteDeleteAsync();

        _output.WriteLine($"[Cleanup] Done");
        await _dbContext.DisposeAsync();
    }

    /// <summary>
    /// FULL END-TO-END TEST:
    /// 1. Create rental in real DB
    /// 2. Generate PDF contract
    /// 3. Send real email via Gmail SMTP
    /// 4. Read email via Gmail IMAP
    /// 5. Verify email content and PDF attachment
    /// </summary>
    [Fact]
    public async Task E2E_CreateRental_GenerateContract_SendEmail_VerifyViaIMAP()
    {
        _output.WriteLine("\n" + new string('═', 60));
        _output.WriteLine("🚀 E2E TEST: Full Rental → Contract → Email → IMAP Verify");
        _output.WriteLine(new string('═', 60) + "\n");

        // Generate unique test ID for this run
        var testRunId = Guid.NewGuid().ToString()[..8].ToUpper();
        _output.WriteLine($"[Test Run ID] {testRunId}\n");

        // ═══════════════════════════════════════════════════════════
        // STEP 1: Create rental in real database
        // ═══════════════════════════════════════════════════════════
        _output.WriteLine("━━━ STEP 1: Create Rental in Database ━━━");
        
        var rentalId = Guid.NewGuid();
        var startDate = DateTime.UtcNow.AddDays(5);
        var endDate = DateTime.UtcNow.AddDays(8);
        var days = 3;
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
            Notes = $"E2E Test {testRunId} - Automatyczny test integracyjny"
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

        _output.WriteLine($"   ✅ Rental created: {rentalId}");
        _output.WriteLine($"   📅 Period: {startDate:dd.MM.yyyy} - {endDate:dd.MM.yyyy} ({days} days)");
        _output.WriteLine($"   💰 Total: {subtotal:F2} PLN");
        _output.WriteLine($"   🏷️ Product: {_testProductName} x{quantity}");
        _output.WriteLine("");

        // Verify in database
        _dbContext.ChangeTracker.Clear();
        var dbRental = await _dbContext.Rentals.AsNoTracking().FirstOrDefaultAsync(r => r.Id == rentalId);
        dbRental.Should().NotBeNull("Rental should exist in database");
        _output.WriteLine($"   ✅ Verified in database\n");

        // ═══════════════════════════════════════════════════════════
        // STEP 2: Generate PDF contract
        // ═══════════════════════════════════════════════════════════
        _output.WriteLine("━━━ STEP 2: Generate PDF Contract ━━━");

        var customer = await _dbContext.Customers.FindAsync(_testCustomerId);
        var products = await _dbContext.Products.Where(p => p.Id == _testProductId).ToListAsync();

        var mockFileStorage = new Mock<IFileStorage>();
        var mockEmailSender = new Mock<IEmailSender>();
        var mockLogger = new Mock<ILogger<QuestPdfContractGenerator>>();

        var contractGenerator = new QuestPdfContractGenerator(
            mockFileStorage.Object, 
            mockEmailSender.Object, 
            mockLogger.Object);

        var pdfBytes = await contractGenerator.GenerateRentalContractAsync(
            rental, rental.Items, customer!, products, _companyInfo);

        pdfBytes.Should().NotBeEmpty();
        var pdfHeader = Encoding.ASCII.GetString(pdfBytes.Take(4).ToArray());
        pdfHeader.Should().Be("%PDF");

        _output.WriteLine($"   ✅ PDF generated: {pdfBytes.Length} bytes");
        _output.WriteLine($"   📄 Valid PDF header: {pdfHeader}");

        // Save PDF locally for inspection
        var localPdfPath = Path.Combine(Path.GetTempPath(), $"e2e_contract_{testRunId}.pdf");
        await File.WriteAllBytesAsync(localPdfPath, pdfBytes);
        _output.WriteLine($"   💾 Saved locally: {localPdfPath}\n");

        // ═══════════════════════════════════════════════════════════
        // STEP 3: Send real email via Gmail SMTP
        // ═══════════════════════════════════════════════════════════
        _output.WriteLine("━━━ STEP 3: Send Email via Gmail SMTP ━━━");

        var emailSubject = $"🧪 E2E Test {testRunId} - Umowa wypożyczenia SportRental";
        var emailSentTime = DateTime.UtcNow;

        // Build email
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("SportRental Test", GmailEmail));
        message.To.Add(new MailboxAddress(customer!.FullName, GmailEmail));
        message.Subject = emailSubject;

        var htmlBody = BuildTestEmailHtml(rental, customer, products.First(), testRunId);

        var builder = new BodyBuilder();
        builder.HtmlBody = htmlBody;
        builder.Attachments.Add($"umowa_{rentalId.ToString()[..8]}.pdf", pdfBytes, new ContentType("application", "pdf"));
        message.Body = builder.ToMessageBody();

        // Send via SMTP
        using (var smtpClient = new MailKit.Net.Smtp.SmtpClient())
        {
            _output.WriteLine($"   📤 Connecting to {SmtpHost}:{SmtpPort}...");
            await smtpClient.ConnectAsync(SmtpHost, SmtpPort, SecureSocketOptions.StartTls);
            
            _output.WriteLine($"   🔐 Authenticating...");
            await smtpClient.AuthenticateAsync(GmailEmail, GmailAppPassword.Replace(" ", ""));
            
            _output.WriteLine($"   ✉️ Sending email...");
            await smtpClient.SendAsync(message);
            
            await smtpClient.DisconnectAsync(true);
        }

        _output.WriteLine($"   ✅ Email sent successfully!");
        _output.WriteLine($"   📧 To: {GmailEmail}");
        _output.WriteLine($"   📋 Subject: {emailSubject}");
        _output.WriteLine($"   📎 Attachment: umowa_{rentalId.ToString()[..8]}.pdf\n");

        // Update rental in DB (use ExecuteUpdate to avoid tracking issues)
        await _dbContext.Rentals
            .Where(r => r.Id == rentalId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.IsEmailSent, true));

        // ═══════════════════════════════════════════════════════════
        // STEP 4: Wait and retrieve email via IMAP
        // ═══════════════════════════════════════════════════════════
        _output.WriteLine("━━━ STEP 4: Retrieve Email via Gmail IMAP ━━━");
        _output.WriteLine($"   ⏳ Waiting 10 seconds for email delivery...");
        await Task.Delay(10000); // Wait for email to arrive

        MimeMessage? receivedEmail = null;
        byte[]? receivedPdfBytes = null;
        int retryCount = 0;
        const int maxRetries = 6;

        using (var imapClient = new ImapClient())
        {
            _output.WriteLine($"   📥 Connecting to {ImapHost}:{ImapPort}...");
            await imapClient.ConnectAsync(ImapHost, ImapPort, SecureSocketOptions.SslOnConnect);
            
            _output.WriteLine($"   🔐 Authenticating...");
            await imapClient.AuthenticateAsync(GmailEmail, GmailAppPassword.Replace(" ", ""));
            
            var inbox = imapClient.Inbox;
            await inbox.OpenAsync(FolderAccess.ReadOnly);
            _output.WriteLine($"   📬 Inbox opened: {inbox.Count} messages");

            // Search for our test email
            while (receivedEmail == null && retryCount < maxRetries)
            {
                retryCount++;
                _output.WriteLine($"   🔍 Searching for email (attempt {retryCount}/{maxRetries})...");

                // Search by subject containing our test ID
                var query = SearchQuery.SubjectContains(testRunId)
                    .And(SearchQuery.DeliveredAfter(emailSentTime.AddMinutes(-5)));
                
                var uids = await inbox.SearchAsync(query);
                _output.WriteLine($"      Found {uids.Count} matching emails");

                if (uids.Count > 0)
                {
                    // Get the most recent one
                    var uid = uids.Last();
                    receivedEmail = await inbox.GetMessageAsync(uid);
                    _output.WriteLine($"      ✅ Email found! UID: {uid}");

                    // Extract PDF attachment
                    foreach (var attachment in receivedEmail.Attachments)
                    {
                        if (attachment is MimePart part && part.FileName?.EndsWith(".pdf") == true)
                        {
                            using var ms = new MemoryStream();
                            await part.Content.DecodeToAsync(ms);
                            receivedPdfBytes = ms.ToArray();
                            _output.WriteLine($"      📎 PDF attachment extracted: {part.FileName} ({receivedPdfBytes.Length} bytes)");
                        }
                    }
                }
                else if (retryCount < maxRetries)
                {
                    _output.WriteLine($"      ⏳ Email not found yet, waiting 5 more seconds...");
                    await Task.Delay(5000);
                }
            }

            await imapClient.DisconnectAsync(true);
        }

        // ═══════════════════════════════════════════════════════════
        // STEP 5: Verify email content and PDF attachment
        // ═══════════════════════════════════════════════════════════
        _output.WriteLine("\n━━━ STEP 5: Verify Email Content ━━━");

        receivedEmail.Should().NotBeNull("Email should be received via IMAP");
        _output.WriteLine($"   ✅ Email received!");
        _output.WriteLine($"   📧 From: {receivedEmail!.From}");
        _output.WriteLine($"   📧 To: {receivedEmail.To}");
        _output.WriteLine($"   📋 Subject: {receivedEmail.Subject}");
        _output.WriteLine($"   📅 Date: {receivedEmail.Date}");

        // Verify subject
        receivedEmail.Subject.Should().Contain(testRunId, "Subject should contain test run ID");
        receivedEmail.Subject.Should().Contain("E2E Test", "Subject should indicate E2E test");
        _output.WriteLine($"   ✅ Subject verified");

        // Verify HTML body contains expected data
        var bodyText = receivedEmail.HtmlBody ?? receivedEmail.TextBody ?? "";
        bodyText.Should().Contain(customer.FullName, "Body should contain customer name");
        bodyText.Should().Contain(rental.TotalAmount.ToString("F2"), "Body should contain total amount");
        bodyText.Should().Contain(testRunId, "Body should contain test run ID");
        _output.WriteLine($"   ✅ Email body verified");
        _output.WriteLine($"      - Contains customer name: {customer.FullName}");
        _output.WriteLine($"      - Contains total: {rental.TotalAmount:F2} PLN");
        _output.WriteLine($"      - Contains test ID: {testRunId}");

        // Verify PDF attachment
        receivedPdfBytes.Should().NotBeNull("PDF attachment should be present");
        receivedPdfBytes!.Length.Should().BeGreaterThan(1000, "PDF should have reasonable size");
        
        var receivedPdfHeader = Encoding.ASCII.GetString(receivedPdfBytes.Take(4).ToArray());
        receivedPdfHeader.Should().Be("%PDF", "Attachment should be valid PDF");
        _output.WriteLine($"   ✅ PDF attachment verified");
        _output.WriteLine($"      - Size: {receivedPdfBytes.Length} bytes");
        _output.WriteLine($"      - Valid PDF: {receivedPdfHeader}");

        // Compare PDF sizes (should be similar)
        var sizeDiff = Math.Abs(pdfBytes.Length - receivedPdfBytes.Length);
        sizeDiff.Should().BeLessThan(1000, "PDF sizes should be similar (allowing for encoding differences)");
        _output.WriteLine($"      - Size difference from original: {sizeDiff} bytes");

        // Save received PDF for manual inspection
        var receivedPdfPath = Path.Combine(Path.GetTempPath(), $"e2e_received_{testRunId}.pdf");
        await File.WriteAllBytesAsync(receivedPdfPath, receivedPdfBytes);
        _output.WriteLine($"   💾 Received PDF saved: {receivedPdfPath}");

        // ═══════════════════════════════════════════════════════════
        // FINAL SUMMARY
        // ═══════════════════════════════════════════════════════════
        _output.WriteLine("\n" + new string('═', 60));
        _output.WriteLine("🎉 E2E TEST PASSED - ALL VERIFICATIONS SUCCESSFUL!");
        _output.WriteLine(new string('═', 60));
        _output.WriteLine($"");
        _output.WriteLine($"   📊 Summary:");
        _output.WriteLine($"   ├─ Test Run ID: {testRunId}");
        _output.WriteLine($"   ├─ Rental ID: {rentalId}");
        _output.WriteLine($"   ├─ Customer: {customer.FullName}");
        _output.WriteLine($"   ├─ Total: {rental.TotalAmount:F2} PLN");
        _output.WriteLine($"   ├─ Email sent: ✅");
        _output.WriteLine($"   ├─ Email received via IMAP: ✅");
        _output.WriteLine($"   ├─ Email content verified: ✅");
        _output.WriteLine($"   ├─ PDF attachment verified: ✅");
        _output.WriteLine($"   └─ PDF size: {receivedPdfBytes.Length} bytes");
        _output.WriteLine($"");
        _output.WriteLine($"   📁 Files saved:");
        _output.WriteLine($"   ├─ Original PDF: {localPdfPath}");
        _output.WriteLine($"   └─ Received PDF: {receivedPdfPath}");
        _output.WriteLine("");
    }

    private string BuildTestEmailHtml(Rental rental, Customer customer, Product product, string testRunId)
    {
        var companyName = _companyInfo?.Name ?? "SportRental";
        
        return $@"
<!DOCTYPE html>
<html lang='pl'>
<head>
    <meta charset='UTF-8'>
</head>
<body style='font-family: Arial, sans-serif; background-color: #f5f5f5; padding: 20px;'>
    <div style='max-width: 600px; margin: 0 auto; background: white; border-radius: 12px; overflow: hidden; box-shadow: 0 4px 6px rgba(0,0,0,0.1);'>
        
        <!-- Header -->
        <div style='background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; padding: 30px; text-align: center;'>
            <h1 style='margin: 0;'>🧪 E2E Test Email</h1>
            <p style='margin: 10px 0 0 0; opacity: 0.9;'>Test ID: {testRunId}</p>
        </div>
        
        <!-- Content -->
        <div style='padding: 30px;'>
            <div style='background: #10b981; color: white; padding: 10px 20px; border-radius: 25px; display: inline-block; margin-bottom: 20px;'>
                ✓ Automatyczny test integracyjny
            </div>
            
            <h2 style='color: #333;'>Potwierdzenie wypożyczenia</h2>
            
            <p>Cześć <strong>{customer.FullName}</strong>,</p>
            
            <p>To jest automatyczny email testowy z systemu SportRental. W załączniku znajduje się umowa wypożyczenia w formacie PDF.</p>
            
            <!-- Rental Details -->
            <div style='background: #f9fafb; border-left: 4px solid #667eea; padding: 20px; margin: 20px 0;'>
                <h3 style='margin-top: 0; color: #667eea;'>📋 Szczegóły wypożyczenia</h3>
                <table style='width: 100%;'>
                    <tr>
                        <td style='padding: 8px 0;'><strong>Nr rezerwacji:</strong></td>
                        <td style='text-align: right;'>#{rental.Id.ToString()[..8].ToUpper()}</td>
                    </tr>
                    <tr>
                        <td style='padding: 8px 0;'><strong>Data rozpoczęcia:</strong></td>
                        <td style='text-align: right;'>{rental.StartDateUtc:dd.MM.yyyy}</td>
                    </tr>
                    <tr>
                        <td style='padding: 8px 0;'><strong>Data zakończenia:</strong></td>
                        <td style='text-align: right;'>{rental.EndDateUtc:dd.MM.yyyy}</td>
                    </tr>
                    <tr>
                        <td style='padding: 8px 0;'><strong>Produkt:</strong></td>
                        <td style='text-align: right;'>{product.Name}</td>
                    </tr>
                    <tr style='border-top: 2px solid #e5e7eb;'>
                        <td style='padding: 12px 0;'><strong style='font-size: 18px;'>RAZEM:</strong></td>
                        <td style='text-align: right; font-size: 18px; color: #667eea; font-weight: bold;'>{rental.TotalAmount:F2} PLN</td>
                    </tr>
                </table>
            </div>
            
            <!-- Test Info -->
            <div style='background: #fef3c7; border-left: 4px solid #f59e0b; padding: 15px; margin: 20px 0;'>
                <p style='margin: 0;'><strong>⚠️ To jest email testowy</strong></p>
                <p style='margin: 10px 0 0 0; font-size: 14px;'>
                    Test ID: <code style='background: #fff; padding: 2px 6px; border-radius: 4px;'>{testRunId}</code><br>
                    Wygenerowano: {DateTime.Now:dd.MM.yyyy HH:mm:ss}
                </p>
            </div>
            
            <p style='color: #666; font-size: 14px;'>
                📎 W załączniku: <strong>umowa_{rental.Id.ToString()[..8]}.pdf</strong>
            </p>
        </div>
        
        <!-- Footer -->
        <div style='background: #f9fafb; padding: 20px; text-align: center; border-top: 1px solid #e5e7eb;'>
            <p style='margin: 0; font-weight: 600;'>{companyName}</p>
            <p style='margin: 5px 0 0 0; color: #666; font-size: 12px;'>
                E2E Integration Test - można bezpiecznie usunąć
            </p>
        </div>
    </div>
</body>
</html>";
    }
}
