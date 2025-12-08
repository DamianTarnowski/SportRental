using FluentAssertions;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using MailKit.Net.Imap;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using MimeKit;
using Xunit;
using Xunit.Abstractions;

namespace SportRental.Api.Tests;

/// <summary>
/// Tests that READ and ANALYZE PDF contracts from emails!
/// Verifies contract content, extracts text, checks for required fields
/// </summary>
public class PdfContractReadTests
{
    private readonly ITestOutputHelper _output;
    private readonly IConfiguration _configuration;

    public PdfContractReadTests(ITestOutputHelper output)
    {
        _output = output;
        
        _configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.Development.json", optional: false)
            .Build();
    }

    [Fact]
    public async Task RealTest_ReadAndAnalyzePdfContract()
    {
        // Arrange
        _output.WriteLine("╔═══════════════════════════════════════════════╗");
        _output.WriteLine("║  📄 ANALIZA PDF UMOWY Z EMAILA               ║");
        _output.WriteLine("╚═══════════════════════════════════════════════╝");
        _output.WriteLine("");

        var testEmail = _configuration["TestAccounts:TestCustomer:Email"] ?? "testklient@op.pl";
        var testPassword = _configuration["TestAccounts:TestCustomer:Password"] ?? throw new InvalidOperationException("Configure TestAccounts:TestCustomer:Password in appsettings.Test.json");
        var senderEmail = _configuration["Email:Smtp:SenderEmail"] ?? "contact.sportrental@op.pl";

        // Act - Connect to IMAP and get latest email with PDF
        using var client = new ImapClient();
        await client.ConnectAsync("imap.poczta.onet.pl", 993, SecureSocketOptions.SslOnConnect);
        await client.AuthenticateAsync(testEmail, testPassword);

        var inbox = client.Inbox;
        await inbox.OpenAsync(MailKit.FolderAccess.ReadOnly);

        _output.WriteLine($"📬 Total emails: {inbox.Count}");
        _output.WriteLine($"🔍 Searching for emails with PDF...");
        _output.WriteLine("");

        // Find email with PDF attachment
        MimePart? pdfAttachment = null;
        MimeMessage? emailMessage = null;

        for (int i = inbox.Count - 1; i >= 0 && pdfAttachment == null; i--)
        {
            var msg = await inbox.GetMessageAsync(i);
            
            // Look for PDF from our sender
            if (msg.From.ToString().Contains(senderEmail, StringComparison.OrdinalIgnoreCase))
            {
                var pdf = msg.Attachments
                    .OfType<MimePart>()
                    .FirstOrDefault(a => a.ContentType.MimeType == "application/pdf");
                
                if (pdf != null)
                {
                    pdfAttachment = pdf;
                    emailMessage = msg;
                    _output.WriteLine($"✅ Found PDF in email from {msg.Date:dd.MM.yyyy HH:mm}");
                    _output.WriteLine($"   Subject: {msg.Subject}");
                    _output.WriteLine($"   PDF: {pdf.FileName}");
                    _output.WriteLine("");
                    break;
                }
            }
        }

        pdfAttachment.Should().NotBeNull("Should find at least one email with PDF attachment");

        // Extract PDF bytes
        using var memory = new MemoryStream();
        await pdfAttachment!.Content.DecodeToAsync(memory);
        var pdfBytes = memory.ToArray();

        _output.WriteLine($"📄 PDF Details:");
        _output.WriteLine($"   Size: {pdfBytes.Length:N0} bytes ({pdfBytes.Length / 1024.0:F2} KB)");
        _output.WriteLine("");

        // Read PDF content using iText7
        _output.WriteLine("🔍 Extracting text from PDF...");
        _output.WriteLine("");

        string pdfText;
        using (var pdfStream = new MemoryStream(pdfBytes))
        using (var pdfReader = new PdfReader(pdfStream))
        using (var pdfDocument = new PdfDocument(pdfReader))
        {
            var strategy = new LocationTextExtractionStrategy();
            var textBuilder = new System.Text.StringBuilder();

            for (int i = 1; i <= pdfDocument.GetNumberOfPages(); i++)
            {
                var page = pdfDocument.GetPage(i);
                var pageText = PdfTextExtractor.GetTextFromPage(page, strategy);
                textBuilder.AppendLine(pageText);
            }

            pdfText = textBuilder.ToString();
        }

        _output.WriteLine("═══════════════════════════════════════════════");
        _output.WriteLine("📄 ZAWARTOŚĆ PDF (PEŁNA UMOWA):");
        _output.WriteLine("═══════════════════════════════════════════════");
        _output.WriteLine("");
        _output.WriteLine(pdfText);
        _output.WriteLine("");
        _output.WriteLine("═══════════════════════════════════════════════");
        _output.WriteLine("");

        // Analyze content
        _output.WriteLine("🔍 ANALIZA ZAWARTOŚCI:");
        _output.WriteLine("");

        var hasTitle = pdfText.Contains("UMOWA WYPOŻYCZENIA", StringComparison.OrdinalIgnoreCase);
        var hasCustomerData = pdfText.Contains("Dane Klienta", StringComparison.OrdinalIgnoreCase);
        var hasRentalDetails = pdfText.Contains("Szczegóły Wypożyczenia", StringComparison.OrdinalIgnoreCase);
        var hasProductList = pdfText.Contains("Wypożyczony Sprzęt", StringComparison.OrdinalIgnoreCase);
        var hasFinancialSummary = pdfText.Contains("Razem", StringComparison.OrdinalIgnoreCase);
        var hasTerms = pdfText.Contains("Warunki Wypożyczenia", StringComparison.OrdinalIgnoreCase);
        var hasPrices = pdfText.Contains("zł", StringComparison.OrdinalIgnoreCase);

        _output.WriteLine($"✅ Tytuł umowy:              {(hasTitle ? "✅ TAK" : "❌ NIE")}");
        _output.WriteLine($"✅ Dane klienta:             {(hasCustomerData ? "✅ TAK" : "❌ NIE")}");
        _output.WriteLine($"✅ Szczegóły wypożyczenia:   {(hasRentalDetails ? "✅ TAK" : "❌ NIE")}");
        _output.WriteLine($"✅ Lista produktów:          {(hasProductList ? "✅ TAK" : "❌ NIE")}");
        _output.WriteLine($"✅ Podsumowanie finansowe:   {(hasFinancialSummary ? "✅ TAK" : "❌ NIE")}");
        _output.WriteLine($"✅ Warunki wypożyczenia:     {(hasTerms ? "✅ TAK" : "❌ NIE")}");
        _output.WriteLine($"✅ Ceny w PLN:               {(hasPrices ? "✅ TAK" : "❌ NIE")}");
        _output.WriteLine("");

        // Check for MISSING company data
        _output.WriteLine("⚠️  CO BRAKUJE W UMOWIE:");
        _output.WriteLine("");

        var hasCompanyName = pdfText.Contains("Wypożyczalnia") || pdfText.Contains("NIP") || pdfText.Contains("REGON");
        var hasCompanyAddress = pdfText.Contains("ul.") && pdfText.Contains("Wypożycz");
        var hasCompanyNip = pdfText.Contains("NIP:");
        var hasCompanyRegon = pdfText.Contains("REGON:");
        var hasCompanyContact = pdfText.Contains("Tel:") || pdfText.Contains("kontakt@");

        if (!hasCompanyName)
            _output.WriteLine("❌ Brak nazwy firmy wypożyczalni");
        if (!hasCompanyAddress)
            _output.WriteLine("❌ Brak adresu wypożyczalni");
        if (!hasCompanyNip)
            _output.WriteLine("❌ Brak NIP wypożyczalni");
        if (!hasCompanyRegon)
            _output.WriteLine("❌ Brak REGON wypożyczalni");
        if (!hasCompanyContact)
            _output.WriteLine("❌ Brak pełnych danych kontaktowych");

        _output.WriteLine("");
        _output.WriteLine("💡 REKOMENDACJE:");
        _output.WriteLine("");
        _output.WriteLine("1. Dodać do modelu Tenant:");
        _output.WriteLine("   • CompanyName (pełna nazwa firmy)");
        _output.WriteLine("   • CompanyAddress (adres siedziby)");
        _output.WriteLine("   • NIP (numer identyfikacji podatkowej)");
        _output.WriteLine("   • REGON (opcjonalnie)");
        _output.WriteLine("   • ContactPhone (telefon kontaktowy)");
        _output.WriteLine("   • ContactEmail (email kontaktowy)");
        _output.WriteLine("");
        _output.WriteLine("2. Zaktualizować panel admin:");
        _output.WriteLine("   • Dodać sekcję 'Dane firmy' w ustawieniach");
        _output.WriteLine("   • Pola do wypełnienia danych wypożyczalni");
        _output.WriteLine("");
        _output.WriteLine("3. Zaktualizować PDF generator:");
        _output.WriteLine("   • Używać danych z Tenant w nagłówku umowy");
        _output.WriteLine("   • Dodać pełne dane firmy po lewej stronie");
        _output.WriteLine("   • Dane klienta po prawej stronie");
        _output.WriteLine("");

        await client.DisconnectAsync(true);

        // Assert
        pdfText.Length.Should().BeGreaterThan(500, "PDF should have substantial content");
        hasTitle.Should().BeTrue("Should have contract title");
        hasCustomerData.Should().BeTrue("Should have customer data");

        _output.WriteLine("═══════════════════════════════════════════════");
        _output.WriteLine("✅ PDF ANALYSIS COMPLETE!");
        _output.WriteLine("═══════════════════════════════════════════════");
    }
}
