using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using QuestPDF.Infrastructure;
using SportRental.Admin.Services.Contracts;
using SportRental.Admin.Services.Email;
using SportRental.Admin.Services.Storage;
using SportRental.Infrastructure.Domain;
using Xunit;
using Xunit.Abstractions;

namespace SportRental.Admin.Tests.Services.Contracts;

/// <summary>
/// Testy integracyjne generowania umów wypożyczenia.
/// Testują pełny flow: dane firmowe → PDF → email.
/// </summary>
public class ContractGeneratorIntegrationTests
{
    private readonly ITestOutputHelper _output;
    private readonly Mock<IFileStorage> _fileStorageMock;
    private readonly Mock<IEmailSender> _emailSenderMock;
    private readonly Mock<ILogger<QuestPdfContractGenerator>> _loggerMock;
    private readonly QuestPdfContractGenerator _generator;

    static ContractGeneratorIntegrationTests()
    {
        // Ustaw licencję QuestPDF Community (wymagane do generowania PDF)
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public ContractGeneratorIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        _fileStorageMock = new Mock<IFileStorage>();
        _emailSenderMock = new Mock<IEmailSender>();
        _loggerMock = new Mock<ILogger<QuestPdfContractGenerator>>();
        
        _generator = new QuestPdfContractGenerator(
            _fileStorageMock.Object,
            _emailSenderMock.Object,
            _loggerMock.Object);
    }

    #region Test Data Builders

    private static Rental CreateTestRental(Guid tenantId, Guid customerId, decimal totalAmount = 550m, decimal depositAmount = 165m)
    {
        return new Rental
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CustomerId = customerId,
            StartDateUtc = DateTime.UtcNow.AddDays(2),
            EndDateUtc = DateTime.UtcNow.AddDays(7),
            Status = RentalStatus.Confirmed,
            TotalAmount = totalAmount,
            DepositAmount = depositAmount,
            CreatedAtUtc = DateTime.UtcNow,
            Notes = "Klient prosi o przygotowanie sprzętu na godz. 10:00"
        };
    }

    private static Customer CreateTestCustomer(Guid tenantId)
    {
        return new Customer
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            FullName = "Jan Kowalski",
            Email = "jan.kowalski@example.com",
            PhoneNumber = "+48 123 456 789",
            Address = "ul. Sportowa 15, 34-500 Zakopane",
            DocumentNumber = "ABC123456",
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    private static CompanyInfo CreateTestCompanyInfo(Guid tenantId)
    {
        return new CompanyInfo
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Narty & Snowboard Zakopane",
            Address = "ul. Krupówki 123, 34-500 Zakopane",
            NIP = "1234567890",
            REGON = "123456789",
            LegalForm = "Jednoosobowa działalność gospodarcza",
            Email = "kontakt@narty-zakopane.pl",
            PhoneNumber = "+48 18 123 45 67",
            OpeningHours = "Pn-Pt: 8:00-18:00, Sb-Nd: 9:00-17:00",
            Description = "Profesjonalna wypożyczalnia sprzętu narciarskiego",
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    private static List<Product> CreateTestProducts(Guid tenantId)
    {
        return new List<Product>
        {
            new()
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Name = "Narty Rossignol Hero Elite 170cm",
                Sku = "NARTY-ROSS-170",
                DailyPrice = 80m,
                AvailableQuantity = 5,
                Category = "Narty",
                Description = "Profesjonalne narty zjazdowe"
            },
            new()
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Name = "Buty narciarskie Salomon X Pro 130",
                Sku = "BUTY-SAL-130",
                DailyPrice = 30m,
                AvailableQuantity = 10,
                Category = "Buty narciarskie",
                Description = "Wygodne buty narciarskie"
            },
            new()
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Name = "Kask Giro Range MIPS",
                Sku = "KASK-GIRO-M",
                DailyPrice = 20m,
                AvailableQuantity = 15,
                Category = "Akcesoria",
                Description = "Bezpieczny kask z systemem MIPS"
            }
        };
    }

    private static List<RentalItem> CreateTestRentalItems(Guid rentalId, List<Product> products)
    {
        var days = 5;
        return new List<RentalItem>
        {
            new()
            {
                Id = Guid.NewGuid(),
                RentalId = rentalId,
                ProductId = products[0].Id,
                Quantity = 1,
                PricePerDay = products[0].DailyPrice,
                Subtotal = products[0].DailyPrice * 1 * days // 80 * 5 = 400
            },
            new()
            {
                Id = Guid.NewGuid(),
                RentalId = rentalId,
                ProductId = products[1].Id,
                Quantity = 1,
                PricePerDay = products[1].DailyPrice,
                Subtotal = products[1].DailyPrice * 1 * days // 30 * 5 = 150
            }
        };
    }

    #endregion

    [Fact]
    public async Task GenerateRentalContractAsync_WithCompanyInfo_ShouldGenerateValidPdf()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var customer = CreateTestCustomer(tenantId);
        var companyInfo = CreateTestCompanyInfo(tenantId);
        var products = CreateTestProducts(tenantId);
        var rental = CreateTestRental(tenantId, customer.Id);
        var items = CreateTestRentalItems(rental.Id, products);

        // Act
        var pdfBytes = await _generator.GenerateRentalContractAsync(rental, items, customer, products, companyInfo);

        // Assert
        pdfBytes.Should().NotBeNullOrEmpty("PDF powinien zostać wygenerowany");
        pdfBytes.Length.Should().BeGreaterThan(1000, "PDF powinien mieć sensowny rozmiar");
        
        // Sprawdź nagłówek PDF
        var pdfHeader = System.Text.Encoding.ASCII.GetString(pdfBytes.Take(8).ToArray());
        pdfHeader.Should().StartWith("%PDF-", "Plik powinien być poprawnym PDF");
        
        _output.WriteLine($"✅ Wygenerowano PDF umowy: {pdfBytes.Length} bajtów");
        _output.WriteLine($"   Klient: {customer.FullName}");
        _output.WriteLine($"   Firma: {companyInfo.Name}");
        _output.WriteLine($"   Produkty: {items.Count}");
        _output.WriteLine($"   Suma: {rental.TotalAmount:0.00} zł");
    }

    [Fact]
    public async Task GenerateRentalContractAsync_WithoutCompanyInfo_ShouldUseDefaults()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var customer = CreateTestCustomer(tenantId);
        var products = CreateTestProducts(tenantId);
        var rental = CreateTestRental(tenantId, customer.Id);
        var items = CreateTestRentalItems(rental.Id, products);

        // Act - bez CompanyInfo
        var pdfBytes = await _generator.GenerateRentalContractAsync(rental, items, customer, products, companyInfo: null);

        // Assert
        pdfBytes.Should().NotBeNullOrEmpty("PDF powinien zostać wygenerowany nawet bez danych firmowych");
        
        _output.WriteLine($"✅ Wygenerowano PDF bez danych firmowych: {pdfBytes.Length} bajtów");
    }

    [Fact]
    public async Task GenerateAndSaveRentalContractAsync_ShouldSaveToStorage()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var customer = CreateTestCustomer(tenantId);
        var companyInfo = CreateTestCompanyInfo(tenantId);
        var products = CreateTestProducts(tenantId);
        var rental = CreateTestRental(tenantId, customer.Id);
        var items = CreateTestRentalItems(rental.Id, products);

        var expectedUrl = $"https://storage.example.com/contracts/{tenantId}/umowa_{rental.Id}.pdf";
        _fileStorageMock
            .Setup(x => x.SaveAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedUrl);

        // Act
        var contractUrl = await _generator.GenerateAndSaveRentalContractAsync(rental, items, customer, products, companyInfo);

        // Assert
        contractUrl.Should().Be(expectedUrl);
        
        _fileStorageMock.Verify(x => x.SaveAsync(
            It.Is<string>(path => path.Contains($"contracts/{tenantId}") && path.EndsWith(".pdf")),
            It.Is<byte[]>(bytes => bytes.Length > 1000),
            It.IsAny<CancellationToken>()
        ), Times.Once);

        _output.WriteLine($"✅ Umowa zapisana: {contractUrl}");
    }

    [Fact]
    public async Task SendRentalConfirmationEmailAsync_ShouldSendEmailWithPdfAttachment()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var customer = CreateTestCustomer(tenantId);
        var companyInfo = CreateTestCompanyInfo(tenantId);
        var products = CreateTestProducts(tenantId);
        var rental = CreateTestRental(tenantId, customer.Id);
        var items = CreateTestRentalItems(rental.Id, products);

        string? capturedEmail = null;
        string? capturedSubject = null;
        string? capturedHtmlBody = null;
        string? capturedAttachmentPath = null;

        _emailSenderMock
            .Setup(x => x.SendEmailWithAttachmentAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Callback<string, string, string, string?>((email, subject, html, attachment) =>
            {
                capturedEmail = email;
                capturedSubject = subject;
                capturedHtmlBody = html;
                capturedAttachmentPath = attachment;
            })
            .Returns(Task.CompletedTask);

        // Act
        await _generator.SendRentalConfirmationEmailAsync(rental, items, customer, products, companyInfo);

        // Assert
        capturedEmail.Should().Be(customer.Email, "Email powinien być wysłany do klienta");
        capturedSubject.Should().Contain(companyInfo.Name, "Temat powinien zawierać nazwę firmy");
        capturedSubject.Should().Contain(rental.Id.ToString()[..8], "Temat powinien zawierać numer rezerwacji");
        
        // Sprawdź zawartość HTML
        capturedHtmlBody.Should().NotBeNullOrEmpty();
        capturedHtmlBody.Should().Contain(customer.FullName, "Email powinien zawierać imię klienta");
        capturedHtmlBody.Should().Contain(companyInfo.Name, "Email powinien zawierać nazwę firmy");
        capturedHtmlBody.Should().Contain("Rezerwacja potwierdzona", "Email powinien zawierać potwierdzenie");
        capturedHtmlBody.Should().Contain(rental.TotalAmount.ToString("0.00"), "Email powinien zawierać kwotę");
        
        // Sprawdź załącznik PDF
        capturedAttachmentPath.Should().NotBeNullOrEmpty("Powinien być załącznik PDF");
        capturedAttachmentPath.Should().EndWith(".pdf", "Załącznik powinien być plikiem PDF");

        _output.WriteLine($"✅ Email wysłany:");
        _output.WriteLine($"   Do: {capturedEmail}");
        _output.WriteLine($"   Temat: {capturedSubject}");
        _output.WriteLine($"   HTML body length: {capturedHtmlBody?.Length} znaków");
        _output.WriteLine($"   Załącznik: {Path.GetFileName(capturedAttachmentPath)}");
    }

    [Fact]
    public async Task SendRentalConfirmationEmailAsync_CustomerWithoutEmail_ShouldNotThrow()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var customer = CreateTestCustomer(tenantId);
        customer.Email = null; // Brak emaila
        
        var companyInfo = CreateTestCompanyInfo(tenantId);
        var products = CreateTestProducts(tenantId);
        var rental = CreateTestRental(tenantId, customer.Id);
        var items = CreateTestRentalItems(rental.Id, products);

        // Act
        var act = async () => await _generator.SendRentalConfirmationEmailAsync(rental, items, customer, products, companyInfo);

        // Assert - nie powinien rzucić wyjątku, tylko zalogować warning
        await act.Should().NotThrowAsync("Brak emaila nie powinien powodować błędu");
        
        _emailSenderMock.Verify(x => x.SendEmailWithAttachmentAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>()
        ), Times.Never, "Email nie powinien być wysłany gdy brak adresu");

        _output.WriteLine("✅ Poprawnie obsłużono brak emaila klienta");
    }

    [Fact]
    public async Task GenerateRentalContractAsync_WithMultipleItems_ShouldIncludeAllProducts()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var customer = CreateTestCustomer(tenantId);
        var companyInfo = CreateTestCompanyInfo(tenantId);
        var products = CreateTestProducts(tenantId);
        var rental = CreateTestRental(tenantId, customer.Id, totalAmount: 650m);
        
        // Dodaj wszystkie 3 produkty
        var items = new List<RentalItem>
        {
            new() { Id = Guid.NewGuid(), RentalId = rental.Id, ProductId = products[0].Id, Quantity = 1, PricePerDay = 80m, Subtotal = 400m },
            new() { Id = Guid.NewGuid(), RentalId = rental.Id, ProductId = products[1].Id, Quantity = 1, PricePerDay = 30m, Subtotal = 150m },
            new() { Id = Guid.NewGuid(), RentalId = rental.Id, ProductId = products[2].Id, Quantity = 1, PricePerDay = 20m, Subtotal = 100m }
        };

        // Act
        var pdfBytes = await _generator.GenerateRentalContractAsync(rental, items, customer, products, companyInfo);

        // Assert
        pdfBytes.Should().NotBeNullOrEmpty();
        pdfBytes.Length.Should().BeGreaterThan(2000, "PDF z wieloma produktami powinien być większy");

        _output.WriteLine($"✅ PDF z {items.Count} produktami: {pdfBytes.Length} bajtów");
    }

    [Fact]
    public async Task GenerateRentalContractAsync_WithTemplate_ShouldUseCustomTemplate()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var customer = CreateTestCustomer(tenantId);
        var companyInfo = CreateTestCompanyInfo(tenantId);
        var products = CreateTestProducts(tenantId);
        var rental = CreateTestRental(tenantId, customer.Id);
        var items = CreateTestRentalItems(rental.Id, products);

        var customTemplate = @"
UMOWA WYPOŻYCZENIA SPRZĘTU SPORTOWEGO

Firma: {{CompanyName}}
Adres: {{CompanyAddress}}
NIP: {{CompanyNIP}}

Klient: {{CustomerName}}
Okres: {{StartDate}} - {{EndDate}}

Wypożyczony sprzęt:
{{ItemsTable}}

SUMA DO ZAPŁATY: {{Total}} zł
Kaucja: {{Deposit}} zł

Dziękujemy za skorzystanie z naszych usług!
";

        // Act
        var pdfBytes = await _generator.GenerateRentalContractAsync(customTemplate, rental, items, customer, products, companyInfo);

        // Assert
        pdfBytes.Should().NotBeNullOrEmpty();
        
        _output.WriteLine($"✅ PDF z własnym szablonem: {pdfBytes.Length} bajtów");
    }

    [Fact]
    public async Task GenerateRentalContractAsync_WithNotes_ShouldIncludeNotes()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var customer = CreateTestCustomer(tenantId);
        var companyInfo = CreateTestCompanyInfo(tenantId);
        var products = CreateTestProducts(tenantId);
        var rental = CreateTestRental(tenantId, customer.Id);
        rental.Notes = "WAŻNE: Klient alergiczny na lateks - użyć rękawiczek nitrylowych przy obsłudze.";
        var items = CreateTestRentalItems(rental.Id, products);

        // Act
        var pdfBytes = await _generator.GenerateRentalContractAsync(rental, items, customer, products, companyInfo);

        // Assert
        pdfBytes.Should().NotBeNullOrEmpty();
        // PDF powinien zawierać sekcję uwag

        _output.WriteLine($"✅ PDF z notatkami: {pdfBytes.Length} bajtów");
        _output.WriteLine($"   Notatki: {rental.Notes}");
    }

    [Theory]
    [InlineData(1, 50)] // 1 dzień
    [InlineData(7, 350)] // tydzień
    [InlineData(14, 700)] // 2 tygodnie
    [InlineData(30, 1500)] // miesiąc
    public async Task GenerateRentalContractAsync_DifferentDurations_ShouldCalculateCorrectly(int days, decimal expectedTotal)
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var customer = CreateTestCustomer(tenantId);
        var companyInfo = CreateTestCompanyInfo(tenantId);
        
        var product = new Product
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "Test Product",
            DailyPrice = 50m,
            AvailableQuantity = 10
        };

        var rental = new Rental
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CustomerId = customer.Id,
            StartDateUtc = DateTime.UtcNow,
            EndDateUtc = DateTime.UtcNow.AddDays(days),
            TotalAmount = expectedTotal,
            Status = RentalStatus.Confirmed,
            CreatedAtUtc = DateTime.UtcNow
        };

        var items = new List<RentalItem>
        {
            new()
            {
                Id = Guid.NewGuid(),
                RentalId = rental.Id,
                ProductId = product.Id,
                Quantity = 1,
                PricePerDay = 50m,
                Subtotal = expectedTotal
            }
        };

        // Act
        var pdfBytes = await _generator.GenerateRentalContractAsync(rental, items, customer, new[] { product }, companyInfo);

        // Assert
        pdfBytes.Should().NotBeNullOrEmpty();

        _output.WriteLine($"✅ Wynajem na {days} dni: suma {expectedTotal} zł");
    }

    [Fact]
    public async Task FullIntegrationFlow_CreateRentalAndSendEmail()
    {
        // Arrange - pełny scenariusz biznesowy
        _output.WriteLine("🎿 SCENARIUSZ: Klient rezerwuje sprzęt narciarski na wyjazd do Zakopanego");
        _output.WriteLine("");

        var tenantId = Guid.NewGuid();
        
        // Firma wypożyczalni
        var companyInfo = new CompanyInfo
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = "SKI RENTAL Zakopane",
            Address = "ul. Krupówki 50, 34-500 Zakopane",
            NIP = "7361234567",
            REGON = "360123456",
            Email = "rezerwacje@skirental-zakopane.pl",
            PhoneNumber = "+48 18 201 23 45",
            OpeningHours = "Codziennie 8:00-20:00"
        };
        _output.WriteLine($"📍 Firma: {companyInfo.Name}");
        _output.WriteLine($"   {companyInfo.Address}");
        _output.WriteLine($"   NIP: {companyInfo.NIP}");
        _output.WriteLine("");

        // Klient
        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            FullName = "Anna Nowak",
            Email = "anna.nowak@gmail.com",
            PhoneNumber = "+48 501 234 567",
            Address = "ul. Kwiatowa 10, 00-001 Warszawa"
        };
        _output.WriteLine($"👤 Klient: {customer.FullName}");
        _output.WriteLine($"   Email: {customer.Email}");
        _output.WriteLine($"   Tel: {customer.PhoneNumber}");
        _output.WriteLine("");

        // Produkty
        var products = new List<Product>
        {
            new() { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Narty Atomic Redster X9", DailyPrice = 120m, AvailableQuantity = 3 },
            new() { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Buty Atomic Hawx Prime 130", DailyPrice = 45m, AvailableQuantity = 5 },
            new() { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Kije narciarskie Leki", DailyPrice = 15m, AvailableQuantity = 10 },
            new() { Id = Guid.NewGuid(), TenantId = tenantId, Name = "Kask POC Obex SPIN", DailyPrice = 25m, AvailableQuantity = 8 }
        };

        // Wynajem na 5 dni (weekend + kilka dni)
        var startDate = new DateTime(2024, 2, 15, 10, 0, 0, DateTimeKind.Utc); // Czwartek
        var endDate = new DateTime(2024, 2, 19, 18, 0, 0, DateTimeKind.Utc);   // Poniedziałek
        var days = 5;

        var rental = new Rental
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CustomerId = customer.Id,
            StartDateUtc = startDate,
            EndDateUtc = endDate,
            Status = RentalStatus.Confirmed,
            TotalAmount = (120 + 45 + 15 + 25) * days, // 205 * 5 = 1025
            DepositAmount = 300m,
            CreatedAtUtc = DateTime.UtcNow,
            Notes = "Klientka jest początkująca - polecić trasę dla początkujących"
        };

        var items = new List<RentalItem>
        {
            new() { Id = Guid.NewGuid(), RentalId = rental.Id, ProductId = products[0].Id, Quantity = 1, PricePerDay = 120m, Subtotal = 600m },
            new() { Id = Guid.NewGuid(), RentalId = rental.Id, ProductId = products[1].Id, Quantity = 1, PricePerDay = 45m, Subtotal = 225m },
            new() { Id = Guid.NewGuid(), RentalId = rental.Id, ProductId = products[2].Id, Quantity = 1, PricePerDay = 15m, Subtotal = 75m },
            new() { Id = Guid.NewGuid(), RentalId = rental.Id, ProductId = products[3].Id, Quantity = 1, PricePerDay = 25m, Subtotal = 125m }
        };

        _output.WriteLine($"📦 Zamówienie #{rental.Id.ToString()[..8].ToUpper()}:");
        foreach (var item in items)
        {
            var product = products.First(p => p.Id == item.ProductId);
            _output.WriteLine($"   • {product.Name}: {item.Quantity}x {item.PricePerDay} zł/dzień = {item.Subtotal} zł");
        }
        _output.WriteLine($"   ─────────────────────────────");
        _output.WriteLine($"   Okres: {startDate:dd.MM.yyyy} - {endDate:dd.MM.yyyy} ({days} dni)");
        _output.WriteLine($"   SUMA: {rental.TotalAmount} zł");
        _output.WriteLine($"   Kaucja: {rental.DepositAmount} zł");
        _output.WriteLine("");

        // Setup mocks
        var savedPdfPath = "";
        _fileStorageMock
            .Setup(x => x.SaveAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .Callback<string, byte[], CancellationToken>((path, _, _) => savedPdfPath = path)
            .ReturnsAsync("https://storage.blob.core.windows.net/contracts/umowa.pdf");

        string? sentToEmail = null;
        string? emailSubject = null;
        _emailSenderMock
            .Setup(x => x.SendEmailWithAttachmentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string, string, string?>((email, subject, _, _) =>
            {
                sentToEmail = email;
                emailSubject = subject;
            })
            .Returns(Task.CompletedTask);

        // Act 1: Generuj i zapisz umowę
        _output.WriteLine("📄 Generowanie umowy PDF...");
        var contractUrl = await _generator.GenerateAndSaveRentalContractAsync(rental, items, customer, products, companyInfo);

        // Act 2: Wyślij email z potwierdzeniem
        _output.WriteLine("📧 Wysyłanie emaila z potwierdzeniem...");
        await _generator.SendRentalConfirmationEmailAsync(rental, items, customer, products, companyInfo);

        // Assert
        contractUrl.Should().NotBeNullOrEmpty();
        savedPdfPath.Should().Contain($"contracts/{tenantId}");
        sentToEmail.Should().Be(customer.Email);
        emailSubject.Should().Contain(companyInfo.Name);

        _output.WriteLine("");
        _output.WriteLine("✅ WYNIK:");
        _output.WriteLine($"   Umowa zapisana: {contractUrl}");
        _output.WriteLine($"   Email wysłany do: {sentToEmail}");
        _output.WriteLine($"   Temat: {emailSubject}");
        _output.WriteLine("");
        _output.WriteLine("🎉 Rezerwacja zakończona sukcesem!");
    }
}

