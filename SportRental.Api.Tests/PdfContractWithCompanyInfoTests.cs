using FluentAssertions;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using SportRental.Api.Services.Contracts;
using SportRental.Infrastructure.Domain;
using Xunit;
using Xunit.Abstractions;

namespace SportRental.Api.Tests;

/// <summary>
/// Tests that verify PDF contracts contain proper company information
/// </summary>
public class PdfContractWithCompanyInfoTests
{
    private readonly ITestOutputHelper _output;
    private readonly Mock<IWebHostEnvironment> _mockEnvironment;
    private readonly Mock<ILogger<PdfContractService>> _mockLogger;
    private readonly PdfContractService _pdfService;

    public PdfContractWithCompanyInfoTests(ITestOutputHelper output)
    {
        _output = output;
        _mockEnvironment = new Mock<IWebHostEnvironment>();
        _mockEnvironment.Setup(e => e.WebRootPath).Returns(Path.GetTempPath());
        
        _mockLogger = new Mock<ILogger<PdfContractService>>();
        _pdfService = new PdfContractService(_mockEnvironment.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task GenerateContractPdf_WithCompanyInfo_ShouldIncludeCompanyDetails()
    {
        // Arrange
        _output.WriteLine("╔═══════════════════════════════════════════════╗");
        _output.WriteLine("║  📄 TEST: PDF Z DANYMI FIRMY                  ║");
        _output.WriteLine("╚═══════════════════════════════════════════════╝");
        _output.WriteLine("");

        var companyInfo = new CompanyInfo
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            Name = "Wypożyczalnia 'Narty & Snowboard' Sp. z o.o.",
            Address = "ul. Krupówki 12/3, 34-500 Zakopane",
            NIP = "7362614562",
            REGON = "012345678",
            Email = "kontakt@nartyzakopane.pl",
            PhoneNumber = "+48 18 201 50 00"
        };

        var rental = new Rental
        {
            Id = Guid.NewGuid(),
            TenantId = companyInfo.TenantId,
            StartDateUtc = DateTime.UtcNow.AddDays(2),
            EndDateUtc = DateTime.UtcNow.AddDays(5),
            TotalAmount = 1080m,
            DepositAmount = 324m,
            Status = RentalStatus.Confirmed
        };

        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            TenantId = companyInfo.TenantId,
            FullName = "Jan Testowy",
            Email = "testklient@op.pl",
            PhoneNumber = "+48 123 456 789",
            DocumentNumber = "TEST123456"
        };

        var product = new Product
        {
            Id = Guid.NewGuid(),
            TenantId = companyInfo.TenantId,
            Name = "Narty Rossignol Hero Elite ST Ti",
            Sku = "SKI-ROSS-001",
            DailyPrice = 120m
        };

        var items = new List<(Product product, int quantity)>
        {
            (product, 3)
        };

        // Act
        _output.WriteLine("📝 Generating PDF with company info...");
        var pdfBytes = await _pdfService.GenerateContractPdfAsync(rental, customer, items, companyInfo);

        _output.WriteLine($"✅ PDF generated: {pdfBytes.Length:N0} bytes ({pdfBytes.Length / 1024.0:F2} KB)");
        _output.WriteLine("");

        // Extract text from PDF
        _output.WriteLine("🔍 Extracting text from PDF...");
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
        _output.WriteLine("📄 ZAWARTOŚĆ PDF (NOWA UMOWA Z DANYMI FIRMY):");
        _output.WriteLine("═══════════════════════════════════════════════");
        _output.WriteLine("");
        _output.WriteLine(pdfText);
        _output.WriteLine("");
        _output.WriteLine("═══════════════════════════════════════════════");
        _output.WriteLine("");

        // Assert - Check for company details
        _output.WriteLine("✅ WERYFIKACJA DANYCH FIRMY W PDF:");
        _output.WriteLine("");

        pdfText.Should().Contain(companyInfo.Name!, "PDF should contain company name");
        _output.WriteLine($"✅ Nazwa firmy:  {companyInfo.Name}");

        pdfText.Should().Contain(companyInfo.Address!, "PDF should contain company address");
        _output.WriteLine($"✅ Adres:        {companyInfo.Address}");

        pdfText.Should().Contain(companyInfo.NIP!, "PDF should contain NIP");
        _output.WriteLine($"✅ NIP:          {companyInfo.NIP}");

        pdfText.Should().Contain(companyInfo.REGON!, "PDF should contain REGON");
        _output.WriteLine($"✅ REGON:        {companyInfo.REGON}");

        pdfText.Should().Contain(companyInfo.Email!, "PDF should contain company email");
        _output.WriteLine($"✅ Email:        {companyInfo.Email}");

        pdfText.Should().Contain(companyInfo.PhoneNumber!, "PDF should contain company phone");
        _output.WriteLine($"✅ Telefon:      {companyInfo.PhoneNumber}");

        _output.WriteLine("");

        // Check for customer details (name may be split in PDF text extraction)
        (pdfText.Contains(customer.FullName) || (pdfText.Contains("Jan") && pdfText.Contains("Testowy")))
            .Should().BeTrue("PDF should contain customer name or name parts");
        pdfText.Should().Contain(customer.Email, "PDF should contain customer email");
        pdfText.Should().Contain(product.Name, "PDF should contain product name");
        
        _output.WriteLine("✅ Dane klienta:  OK");
        _output.WriteLine("✅ Produkty:      OK");

        _output.WriteLine("╔═══════════════════════════════════════════════╗");
        _output.WriteLine("║  ✅ SUKCES! PDF MA WSZYSTKIE DANE FIRMY!      ║");
        _output.WriteLine("╚═══════════════════════════════════════════════╝");
    }

    [Fact]
    public async Task GenerateContractPdf_WithoutCompanyInfo_ShouldWorkButUseFallbacks()
    {
        // Arrange
        _output.WriteLine("╔═══════════════════════════════════════════════╗");
        _output.WriteLine("║  📄 TEST: PDF BEZ CompanyInfo (fallback)      ║");
        _output.WriteLine("╚═══════════════════════════════════════════════╝");
        _output.WriteLine("");

        var rental = new Rental
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            StartDateUtc = DateTime.UtcNow.AddDays(2),
            EndDateUtc = DateTime.UtcNow.AddDays(5),
            TotalAmount = 1080m,
            DepositAmount = 324m,
            Status = RentalStatus.Confirmed
        };

        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            TenantId = rental.TenantId,
            FullName = "Jan Testowy",
            Email = "test@test.pl",
            PhoneNumber = "+48 123 456 789",
            DocumentNumber = "TEST123456"
        };

        var product = new Product
        {
            Id = Guid.NewGuid(),
            TenantId = rental.TenantId,
            Name = "Test Product",
            Sku = "TEST-001",
            DailyPrice = 100m
        };

        var items = new List<(Product product, int quantity)> { (product, 1) };

        // Act
        _output.WriteLine("📝 Generating PDF WITHOUT company info...");
        var pdfBytes = await _pdfService.GenerateContractPdfAsync(rental, customer, items, companyInfo: null);

        _output.WriteLine($"✅ PDF generated: {pdfBytes.Length:N0} bytes");
        _output.WriteLine("");

        // Assert
        pdfBytes.Length.Should().BeGreaterThan(1000, "PDF should be generated even without company info");

        // Extract text to verify fallback values
        string pdfText;
        using (var pdfStream = new MemoryStream(pdfBytes))
        using (var pdfReader = new PdfReader(pdfStream))
        using (var pdfDocument = new PdfDocument(pdfReader))
        {
            var strategy = new LocationTextExtractionStrategy();
            pdfText = PdfTextExtractor.GetTextFromPage(pdfDocument.GetPage(1), strategy);
        }

        pdfText.Should().Contain("SportRental", "PDF should use fallback company name");
        _output.WriteLine("✅ Fallback nazwa: SportRental");
        
        pdfText.Should().Contain("kontakt@sportrental.pl", "PDF should use fallback email");
        _output.WriteLine("✅ Fallback email: kontakt@sportrental.pl");

        _output.WriteLine("");
        _output.WriteLine("╔═══════════════════════════════════════════════╗");
        _output.WriteLine("║  ✅ SUKCES! PDF działa z fallback values!     ║");
        _output.WriteLine("╚═══════════════════════════════════════════════╝");
    }
}
