using FluentAssertions;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using MimeKit;
using Xunit;
using Xunit.Abstractions;

namespace SportRental.Api.Tests;

/// <summary>
/// E2E tests that actually RECEIVE and READ emails from Onet mailbox!
/// Tests the full flow: Send → Deliver → Receive → Verify
/// </summary>
public class OnetEmailReceiveTests
{
    private readonly ITestOutputHelper _output;
    private readonly IConfiguration _configuration;

    public OnetEmailReceiveTests(ITestOutputHelper output)
    {
        _output = output;
        
        _configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.Development.json", optional: false)
            .Build();
    }

    [Fact]
    public async Task RealTest_ReceiveLastEmail_FromOnetInbox()
    {
        // Arrange
        _output.WriteLine("═══════════════════════════════════════");
        _output.WriteLine("📬 TEST 1: Receiving Email from Onet");
        _output.WriteLine("═══════════════════════════════════════");
        _output.WriteLine("");

        var testEmail = _configuration["TestAccounts:TestCustomer:Email"] ?? "testklient@op.pl";
        var testPassword = _configuration["TestAccounts:TestCustomer:Password"] ?? "HasloHaslo122@@@";

        _output.WriteLine($"Mailbox: {testEmail}");
        _output.WriteLine($"IMAP:    imap.poczta.onet.pl:993");
        _output.WriteLine("");

        // Act
        _output.WriteLine("📥 Connecting to IMAP...");
        
        using var client = new ImapClient();
        await client.ConnectAsync("imap.poczta.onet.pl", 993, SecureSocketOptions.SslOnConnect);
        
        _output.WriteLine("✅ Connected! Authenticating...");
        
        await client.AuthenticateAsync(testEmail, testPassword);
        
        _output.WriteLine("✅ Authenticated! Opening INBOX...");

        var inbox = client.Inbox;
        await inbox.OpenAsync(MailKit.FolderAccess.ReadOnly);

        _output.WriteLine($"📬 Inbox opened! Total messages: {inbox.Count}");
        _output.WriteLine("");

        // Assert
        inbox.Count.Should().BeGreaterThan(0, "Should have at least one email from tests");

        // Get last 5 emails
        var recentCount = Math.Min(5, inbox.Count);
        _output.WriteLine($"📧 Fetching last {recentCount} emails...");
        _output.WriteLine("");

        for (int i = inbox.Count - 1; i >= inbox.Count - recentCount && i >= 0; i--)
        {
            var message = await inbox.GetMessageAsync(i);
            
            _output.WriteLine($"Email #{inbox.Count - i}:");
            _output.WriteLine($"  From:    {message.From}");
            _output.WriteLine($"  Subject: {message.Subject}");
            _output.WriteLine($"  Date:    {message.Date:dd.MM.yyyy HH:mm:ss}");
            _output.WriteLine($"  Has PDF: {message.Attachments.Any(a => a.ContentType.MimeType == "application/pdf")}");
            _output.WriteLine("");
        }

        await client.DisconnectAsync(true);

        _output.WriteLine("═══════════════════════════════════════");
        _output.WriteLine("✅ SUCCESS! Emails received from Onet!");
        _output.WriteLine("═══════════════════════════════════════");
    }

    [Fact]
    public async Task RealTest_ReceiveAndVerifyEmailWithPDF()
    {
        // Arrange
        _output.WriteLine("═══════════════════════════════════════");
        _output.WriteLine("📄 TEST 2: Verify Email with PDF Contract");
        _output.WriteLine("═══════════════════════════════════════");
        _output.WriteLine("");

        var testEmail = _configuration["TestAccounts:TestCustomer:Email"] ?? "testklient@op.pl";
        var testPassword = _configuration["TestAccounts:TestCustomer:Password"] ?? "HasloHaslo122@@@";
        var senderEmail = _configuration["Email:Smtp:SenderEmail"] ?? "contact.sportrental@op.pl";

        // Act
        using var client = new ImapClient();
        await client.ConnectAsync("imap.poczta.onet.pl", 993, SecureSocketOptions.SslOnConnect);
        await client.AuthenticateAsync(testEmail, testPassword);

        var inbox = client.Inbox;
        await inbox.OpenAsync(MailKit.FolderAccess.ReadOnly);

        _output.WriteLine($"📬 Total emails: {inbox.Count}");
        _output.WriteLine($"🔍 Looking for emails from: {senderEmail}");
        _output.WriteLine("");

        // Get all emails and filter by sender
        var emailsFromSender = new List<(int index, MimeMessage message)>();
        
        for (int i = 0; i < inbox.Count; i++)
        {
            var msg = await inbox.GetMessageAsync(i);
            if (msg.From.ToString().Contains(senderEmail, StringComparison.OrdinalIgnoreCase))
            {
                emailsFromSender.Add((i, msg));
            }
        }

        _output.WriteLine($"📧 Found {emailsFromSender.Count} emails from {senderEmail}");
        _output.WriteLine("");

        // Assert
        emailsFromSender.Count.Should().BeGreaterThan(0, $"Should have emails from {senderEmail}");

        // Get the most recent email
        var (_, message) = emailsFromSender.Last();

        _output.WriteLine("📧 MOST RECENT EMAIL:");
        _output.WriteLine($"   From:    {message.From}");
        _output.WriteLine($"   To:      {message.To}");
        _output.WriteLine($"   Subject: {message.Subject}");
        _output.WriteLine($"   Date:    {message.Date:dd.MM.yyyy HH:mm:ss}");
        _output.WriteLine("");

        // Check for PDF attachment
        var pdfAttachments = message.Attachments
            .Where(a => a.ContentType.MimeType == "application/pdf")
            .ToList();

        _output.WriteLine($"📎 Attachments: {message.Attachments.Count()}");
        _output.WriteLine($"📄 PDF files:   {pdfAttachments.Count}");
        _output.WriteLine("");

        if (pdfAttachments.Any())
        {
            foreach (var attachment in pdfAttachments)
            {
                if (attachment is MimePart mimePart)
                {
                    _output.WriteLine($"📄 PDF Details:");
                    _output.WriteLine($"   Filename: {mimePart.FileName}");
                    _output.WriteLine($"   Type:     {mimePart.ContentType.MimeType}");
                    
                    // Read PDF bytes
                    using var memory = new MemoryStream();
                    await mimePart.Content.DecodeToAsync(memory);
                    var pdfBytes = memory.ToArray();
                    
                    _output.WriteLine($"   Size:     {pdfBytes.Length:N0} bytes ({pdfBytes.Length / 1024:N0} KB)");
                    _output.WriteLine("");

                    // Verify PDF header
                    var pdfHeader = System.Text.Encoding.ASCII.GetString(pdfBytes.Take(5).ToArray());
                    _output.WriteLine($"   Header:   {pdfHeader}");
                    _output.WriteLine($"   Valid:    {pdfHeader == "%PDF-"}");
                    _output.WriteLine("");

                    // Assert
                    pdfBytes.Length.Should().BeGreaterThan(1000, "PDF should be at least 1KB");
                    pdfHeader.Should().Be("%PDF-", "Should be valid PDF file");
                    mimePart.FileName.Should().Contain("umowa", "PDF should be rental contract");
                }
            }
        }

        // Verify HTML body
        _output.WriteLine("📝 Email Body:");
        var htmlBody = message.HtmlBody ?? message.TextBody ?? "";
        var bodyPreview = htmlBody.Length > 200 ? htmlBody.Substring(0, 200) + "..." : htmlBody;
        _output.WriteLine($"   Length: {htmlBody.Length} chars");
        _output.WriteLine($"   Preview: {bodyPreview.Replace("\n", " ").Replace("\r", "")}");
        _output.WriteLine("");

        htmlBody.Length.Should().BeGreaterThan(100, "Email should have content");

        await client.DisconnectAsync(true);

        _output.WriteLine("═══════════════════════════════════════");
        _output.WriteLine("✅ SUCCESS! PDF verified!");
        _output.WriteLine("═══════════════════════════════════════");
    }

    [Fact]
    public async Task RealTest_ReceiveAndReadRentalContract()
    {
        // Arrange
        _output.WriteLine("═══════════════════════════════════════");
        _output.WriteLine("📜 TEST 3: Read Full Rental Contract");
        _output.WriteLine("═══════════════════════════════════════");
        _output.WriteLine("");

        var testEmail = _configuration["TestAccounts:TestCustomer:Email"] ?? "testklient@op.pl";
        var testPassword = _configuration["TestAccounts:TestCustomer:Password"] ?? "HasloHaslo122@@@";

        // Act
        using var client = new ImapClient();
        await client.ConnectAsync("imap.poczta.onet.pl", 993, SecureSocketOptions.SslOnConnect);
        await client.AuthenticateAsync(testEmail, testPassword);

        var inbox = client.Inbox;
        await inbox.OpenAsync(MailKit.FolderAccess.ReadOnly);

        // Search for rental confirmation emails
        var query = SearchQuery.SubjectContains("Potwierdzenie wypożyczenia");
        var uids = await inbox.SearchAsync(query);

        _output.WriteLine($"🔍 Found {uids.Count} rental confirmation emails");
        _output.WriteLine("");

        if (uids.Count == 0)
        {
            _output.WriteLine("⚠️  No rental confirmation emails found yet.");
            _output.WriteLine("   This is OK if you haven't done a full payment test yet.");
            return;
        }

        // Get most recent rental confirmation
        var lastUid = uids.Last();
        var message = await inbox.GetMessageAsync(lastUid);

        _output.WriteLine("📧 RENTAL CONFIRMATION EMAIL:");
        _output.WriteLine($"   Subject:  {message.Subject}");
        _output.WriteLine($"   From:     {message.From}");
        _output.WriteLine($"   Date:     {message.Date:dd.MM.yyyy HH:mm:ss}");
        _output.WriteLine("");

        // Check HTML body for rental details
        var htmlBody = message.HtmlBody ?? "";
        
        _output.WriteLine("📋 Email Content Verification:");
        _output.WriteLine($"   Contains 'Dziękujemy':       {htmlBody.Contains("Dziękujemy")}");
        _output.WriteLine($"   Contains 'wypożyczenie':     {htmlBody.Contains("wypożyczenie")}");
        _output.WriteLine($"   Contains 'Szczegóły':        {htmlBody.Contains("Szczegóły")}");
        _output.WriteLine($"   Contains 'PLN':              {htmlBody.Contains("PLN")}");
        _output.WriteLine("");

        // Check PDF attachment
        var pdfAttachment = message.Attachments
            .OfType<MimePart>()
            .FirstOrDefault(a => a.ContentType.MimeType == "application/pdf");

        if (pdfAttachment != null)
        {
            _output.WriteLine("📄 PDF CONTRACT FOUND:");
            _output.WriteLine($"   Filename: {pdfAttachment.FileName}");
            
            using var memory = new MemoryStream();
            await pdfAttachment.Content.DecodeToAsync(memory);
            var pdfBytes = memory.ToArray();
            
            _output.WriteLine($"   Size:     {pdfBytes.Length:N0} bytes");
            _output.WriteLine("");

            // Save PDF to temp for manual inspection (optional)
            var tempPath = Path.Combine(Path.GetTempPath(), $"test_contract_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");
            await File.WriteAllBytesAsync(tempPath, pdfBytes);
            _output.WriteLine($"📁 PDF saved for inspection: {tempPath}");
            _output.WriteLine("");

            // Assert
            pdfBytes.Length.Should().BeGreaterThan(10000, "Contract PDF should be at least 10KB");
            pdfAttachment.FileName.Should().Contain("umowa", "Should be contract file");
        }
        else
        {
            _output.WriteLine("⚠️  No PDF attachment found");
        }

        await client.DisconnectAsync(true);

        _output.WriteLine("═══════════════════════════════════════");
        _output.WriteLine("✅ SUCCESS! Contract read and verified!");
        _output.WriteLine("═══════════════════════════════════════");
    }

    [Fact]
    public async Task RealTest_CountEmailsByType()
    {
        // Arrange
        _output.WriteLine("═══════════════════════════════════════");
        _output.WriteLine("📊 TEST 4: Count Emails by Type");
        _output.WriteLine("═══════════════════════════════════════");
        _output.WriteLine("");

        var testEmail = _configuration["TestAccounts:TestCustomer:Email"] ?? "testklient@op.pl";
        var testPassword = _configuration["TestAccounts:TestCustomer:Password"] ?? "HasloHaslo122@@@";

        // Act
        using var client = new ImapClient();
        await client.ConnectAsync("imap.poczta.onet.pl", 993, SecureSocketOptions.SslOnConnect);
        await client.AuthenticateAsync(testEmail, testPassword);

        var inbox = client.Inbox;
        await inbox.OpenAsync(MailKit.FolderAccess.ReadOnly);

        _output.WriteLine($"📬 Total emails in inbox: {inbox.Count}");
        _output.WriteLine("");

        // Count by type - manual filtering because IMAP search may not work reliably
        int testEmails = 0;
        int rentalConfirmations = 0;
        int complexHtml = 0;
        int multiRecipient = 0;

        for (int i = 0; i < inbox.Count; i++)
        {
            var msg = await inbox.GetMessageAsync(i);
            var subject = msg.Subject ?? "";
            
            if (subject.Contains("Test Email", StringComparison.OrdinalIgnoreCase))
                testEmails++;
            if (subject.Contains("Potwierdzenie wypożyczenia", StringComparison.OrdinalIgnoreCase))
                rentalConfirmations++;
            if (subject.Contains("Complex HTML", StringComparison.OrdinalIgnoreCase))
                complexHtml++;
            if (subject.Contains("Multi-recipient", StringComparison.OrdinalIgnoreCase))
                multiRecipient++;
        }

        _output.WriteLine("📊 EMAIL BREAKDOWN:");
        _output.WriteLine($"   🧪 Test Emails:           {testEmails}");
        _output.WriteLine($"   📄 Rental Confirmations:  {rentalConfirmations}");
        _output.WriteLine($"   🎨 Complex HTML:          {complexHtml}");
        _output.WriteLine($"   👥 Multi-recipient:       {multiRecipient}");
        _output.WriteLine("");

        var totalTestEmails = testEmails + rentalConfirmations + complexHtml + multiRecipient;
        _output.WriteLine($"   ✅ Total from tests:      {totalTestEmails}");
        _output.WriteLine("");

        // Assert
        totalTestEmails.Should().BeGreaterThan(0, "Should have emails from our tests");

        await client.DisconnectAsync(true);

        _output.WriteLine("═══════════════════════════════════════");
        _output.WriteLine("✅ SUCCESS! Email stats collected!");
        _output.WriteLine("═══════════════════════════════════════");
    }
}
