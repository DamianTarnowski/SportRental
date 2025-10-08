using SportRental.Infrastructure.Domain;
using SportRental.Admin.Services.Contracts;
using SportRental.Admin.Services.Email;
using SportRental.Admin.Services.Storage;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using QuestPDF.Infrastructure;

namespace SportRental.Admin.Tests.Services.Contracts;

public class QuestPdfContractGeneratorTests
{
    private readonly Mock<IFileStorage> _fileStorageMock;
    private readonly Mock<IEmailSender> _emailSenderMock;
    private readonly Mock<ILogger<QuestPdfContractGenerator>> _loggerMock;
    private readonly QuestPdfContractGenerator _contractGenerator;

    public QuestPdfContractGeneratorTests()
    {
        // Set QuestPDF license for testing
        QuestPDF.Settings.License = LicenseType.Community;

        _fileStorageMock = new Mock<IFileStorage>();
        _emailSenderMock = new Mock<IEmailSender>();
        _loggerMock = new Mock<ILogger<QuestPdfContractGenerator>>();

        // Setup mock file storage
        _fileStorageMock.Setup(fs => fs.SaveAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string path, byte[] data, CancellationToken ct) => $"https://localhost/storage/{path}");

        _contractGenerator = new QuestPdfContractGenerator(_fileStorageMock.Object, _emailSenderMock.Object, _loggerMock.Object);
    }

    [Fact]
    public void Constructor_WithValidParameters_ShouldInitialize()
    {
        // Act & Assert
        _contractGenerator.Should().NotBeNull();
    }

    [Fact]
    public async Task GenerateAndSaveRentalContractAsync_WithValidData_ShouldReturnContractUrl()
    {
        // Arrange
        var rental = CreateTestRental();
        var customer = CreateTestCustomer();
        var products = CreateTestProducts();
        var rentalItems = CreateTestRentalItems(products);

        // Act
        var result = await _contractGenerator.GenerateAndSaveRentalContractAsync(rental, rentalItems, customer, products);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().StartWith($"https://localhost/storage/contracts/{rental.TenantId}");
        result.Should().Contain(".pdf");
        
        // Verify file storage was called
        _fileStorageMock.Verify(fs => fs.SaveAsync(
            It.Is<string>(path => path.StartsWith($"contracts/{rental.TenantId}") && path.Contains(rental.Id.ToString())), 
            It.IsAny<byte[]>(), 
            It.IsAny<CancellationToken>()), 
            Times.Once);
    }

    [Fact]
    public async Task GenerateAndSaveRentalContractAsync_WithNullRental_ShouldThrowArgumentNullException()
    {
        // Arrange
        var customer = CreateTestCustomer();
        var products = CreateTestProducts();
        var rentalItems = CreateTestRentalItems(products);

        // Act & Assert
        await _contractGenerator.Invoking(x => x.GenerateAndSaveRentalContractAsync(null!, rentalItems, customer, products))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task GenerateAndSaveRentalContractAsync_WithNullCustomer_ShouldThrowArgumentNullException()
    {
        // Arrange
        var rental = CreateTestRental();
        var products = CreateTestProducts();
        var rentalItems = CreateTestRentalItems(products);

        // Act & Assert
        await _contractGenerator.Invoking(x => x.GenerateAndSaveRentalContractAsync(rental, rentalItems, null!, products))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task GenerateAndSaveRentalContractAsync_WithEmptyRentalItems_ShouldThrowArgumentException()
    {
        // Arrange
        var rental = CreateTestRental();
        var customer = CreateTestCustomer();
        var products = CreateTestProducts();
        var emptyRentalItems = new List<RentalItem>();

        // Act & Assert
        await _contractGenerator.Invoking(x => x.GenerateAndSaveRentalContractAsync(rental, emptyRentalItems, customer, products))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SendRentalContractByEmailAsync_WithValidData_ShouldCallEmailSender()
    {
        // Arrange
        var rental = CreateTestRental();
        var customer = CreateTestCustomer();
        var products = CreateTestProducts();
        var rentalItems = CreateTestRentalItems(products);

        // Act
        await _contractGenerator.SendRentalContractByEmailAsync(rental, rentalItems, customer, products);

        // Assert
        _emailSenderMock.Verify(
            x => x.SendRentalContractAsync(
                customer.Email!,
                customer.FullName,
                It.IsAny<byte[]>()),
            Times.Once);
    }

    [Fact]
    public async Task SendRentalContractByEmailAsync_WithCustomerWithoutEmail_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var rental = CreateTestRental();
        var customer = CreateTestCustomer();
        customer.Email = null;
        var products = CreateTestProducts();
        var rentalItems = CreateTestRentalItems(products);

        // Act & Assert
        await _contractGenerator.Invoking(x => x.SendRentalContractByEmailAsync(rental, rentalItems, customer, products))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage("*email*");
    }

    [Fact]
    public async Task SendRentalContractByEmailAsync_WithEmailSenderFailure_ShouldPropagateException()
    {
        // Arrange
        var rental = CreateTestRental();
        var customer = CreateTestCustomer();
        var products = CreateTestProducts();
        var rentalItems = CreateTestRentalItems(products);

        _emailSenderMock
            .Setup(x => x.SendRentalContractAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>()))
            .ThrowsAsync(new InvalidOperationException("Email service unavailable"));

        // Act & Assert
        await _contractGenerator.Invoking(x => x.SendRentalContractByEmailAsync(rental, rentalItems, customer, products))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Email service unavailable");
    }

    private static Rental CreateTestRental()
    {
        return new Rental
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            StartDateUtc = DateTime.UtcNow,
            EndDateUtc = DateTime.UtcNow.AddDays(7),
            Status = RentalStatus.Active,
            TotalAmount = 350.00m,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    private static Customer CreateTestCustomer()
    {
        return new Customer
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            FullName = "Jan Kowalski",
            Email = "jan.kowalski@test.com",
            PhoneNumber = "+48123456789",
            Address = "ul. Testowa 123, 00-001 Warszawa",
            Notes = "Klient regularny"
        };
    }

    private static List<Product> CreateTestProducts()
    {
        return new List<Product>
        {
            new Product
            {
                Id = Guid.NewGuid(),
                TenantId = Guid.NewGuid(),
                Name = "Rower górski Trek",
                Sku = "BIKE001",
                DailyPrice = 50.00m,
                AvailableQuantity = 5,
                Category = "Rowery",
                ImageUrl = "https://example.com/bike.jpg"
            },
            new Product
            {
                Id = Guid.NewGuid(),
                TenantId = Guid.NewGuid(),
                Name = "Kask rowerowy",
                Sku = "HELMET001",
                DailyPrice = 10.00m,
                AvailableQuantity = 10,
                Category = "Akcesoria"
            }
        };
    }

    private static List<RentalItem> CreateTestRentalItems(List<Product> products)
    {
        return new List<RentalItem>
        {
            new RentalItem
            {
                Id = Guid.NewGuid(),
                RentalId = Guid.NewGuid(),
                ProductId = products[0].Id,
                Quantity = 1,
                PricePerDay = products[0].DailyPrice,
                Subtotal = products[0].DailyPrice * 7 // 7 days
            },
            new RentalItem
            {
                Id = Guid.NewGuid(),
                RentalId = Guid.NewGuid(),
                ProductId = products[1].Id,
                Quantity = 2,
                PricePerDay = products[1].DailyPrice,
                Subtotal = products[1].DailyPrice * 2 * 7 // 2 items * 7 days
            }
        };
    }
}
