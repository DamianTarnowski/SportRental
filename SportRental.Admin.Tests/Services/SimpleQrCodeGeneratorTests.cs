using SportRental.Admin.Services.QrCode;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text;
using System.Text.Json;
using System.Globalization;

namespace SportRental.Admin.Tests.Services;

public class SimpleQrCodeGeneratorTests
{
    private readonly Mock<ILogger<SimpleQrCodeGenerator>> _loggerMock;
    private readonly SimpleQrCodeGenerator _qrCodeGenerator;

    public SimpleQrCodeGeneratorTests()
    {
        _loggerMock = new Mock<ILogger<SimpleQrCodeGenerator>>();
        _qrCodeGenerator = new SimpleQrCodeGenerator(_loggerMock.Object);
    }

    [Fact]
    public async Task GenerateQrCodeAsync_WithSimpleData_ShouldReturnBase64DataUrl()
    {
        // Arrange
        var data = "Test QR Code Data";

        // Act
        var result = await _qrCodeGenerator.GenerateQrCodeAsync(data);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().StartWith("data:text/plain;base64,");
        
        // Verify we can decode the data back
        var base64Part = result.Substring("data:text/plain;base64,".Length);
        var decodedBytes = Convert.FromBase64String(base64Part);
        var decodedData = Encoding.UTF8.GetString(decodedBytes);
        decodedData.Should().Be(data);
    }

    [Fact]
    public async Task GenerateQrCodeAsync_WithLongData_ShouldHandleCorrectly()
    {
        // Arrange
        var longData = new string('A', 1000);

        // Act
        var result = await _qrCodeGenerator.GenerateQrCodeAsync(longData);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().StartWith("data:text/plain;base64,");
        
        // Verify data integrity
        var base64Part = result.Substring("data:text/plain;base64,".Length);
        var decodedBytes = Convert.FromBase64String(base64Part);
        var decodedData = Encoding.UTF8.GetString(decodedBytes);
        decodedData.Should().Be(longData);
    }

    [Fact]
    public async Task GenerateQrCodeAsync_WithSpecialCharacters_ShouldHandleCorrectly()
    {
        // Arrange
        var data = "Special: Ä…Ä™Ä‡Ĺ‚Ĺ„ĂłĹ›ĹşĹĽ !@#$%^&*()";

        // Act
        var result = await _qrCodeGenerator.GenerateQrCodeAsync(data);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().StartWith("data:text/plain;base64,");
        
        // Verify special characters are preserved
        var base64Part = result.Substring("data:text/plain;base64,".Length);
        var decodedBytes = Convert.FromBase64String(base64Part);
        var decodedData = Encoding.UTF8.GetString(decodedBytes);
        decodedData.Should().Be(data);
    }

    [Fact]
    public async Task GenerateQrCodeAsync_WithCustomSize_ShouldIgnoreSizeParameter()
    {
        // Arrange
        var data = "Size test data";
        var customSize = 400;

        // Act
        var result = await _qrCodeGenerator.GenerateQrCodeAsync(data, customSize);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().StartWith("data:text/plain;base64,");
        // Size parameter is ignored in this simple implementation
    }

    [Fact]
    public async Task GenerateQrCodeAsync_ShouldLogInformation()
    {
        // Arrange
        var data = "Log test data";

        // Act
        await _qrCodeGenerator.GenerateQrCodeAsync(data);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Generated QR code for data")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task GenerateQrCodeAsync_WithLongData_ShouldTruncateInLog()
    {
        // Arrange
        var longData = new string('B', 100); // Longer than 50 characters

        // Act
        await _qrCodeGenerator.GenerateQrCodeAsync(longData);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("...")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task GenerateQrCodeBytesAsync_WithSimpleData_ShouldReturnBytes()
    {
        // Arrange
        var data = "Bytes test data";

        // Act
        var result = await _qrCodeGenerator.GenerateQrCodeBytesAsync(data);

        // Assert
        result.Should().NotBeNull();
        result.Should().NotBeEmpty();
        
        // Verify bytes represent the original data
        var decodedData = Encoding.UTF8.GetString(result);
        decodedData.Should().Be(data);
    }

    [Fact]
    public async Task GenerateQrCodeBytesAsync_WithCustomSize_ShouldIgnoreSizeParameter()
    {
        // Arrange
        var data = "Bytes size test";
        var customSize = 300;

        // Act
        var result = await _qrCodeGenerator.GenerateQrCodeBytesAsync(data, customSize);

        // Assert
        result.Should().NotBeNull();
        result.Should().NotBeEmpty();
        
        var decodedData = Encoding.UTF8.GetString(result);
        decodedData.Should().Be(data);
    }

    [Fact]
    public async Task GenerateQrCodeBytesAsync_ShouldLogInformation()
    {
        // Arrange
        var data = "Bytes log test";

        // Act
        await _qrCodeGenerator.GenerateQrCodeBytesAsync(data);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Generated QR code bytes for data")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void GenerateProductQrCodeData_WithValidInput_ShouldReturnValidJson()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var productName = "Test Product";
        var sku = "TEST-001";

        // Act
        var result = _qrCodeGenerator.GenerateProductQrCodeData(productId, productName, sku);

        // Assert
        result.Should().NotBeNullOrEmpty();
        
        // Should be valid JSON
        var qrData = JsonSerializer.Deserialize<JsonElement>(result);
        qrData.GetProperty("Type").GetString().Should().Be("Product");
        qrData.GetProperty("ProductId").GetString().Should().Be(productId.ToString());
        qrData.GetProperty("Name").GetString().Should().Be(productName);
        qrData.GetProperty("Sku").GetString().Should().Be(sku);
        qrData.GetProperty("GeneratedAt").GetString().Should().NotBeNullOrEmpty();
        
        // Verify GeneratedAt is a valid datetime
        var generatedAtLocal = DateTime.Parse(qrData.GetProperty("GeneratedAt").GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToLocalTime();
        generatedAtLocal.Should().BeCloseTo(DateTime.Now, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void GenerateProductQrCodeData_WithSpecialCharactersInName_ShouldHandleCorrectly()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var productName = "Narty Alpine \"Pro\" - model 2024";
        var sku = "NARTY-PRO-2024";

        // Act
        var result = _qrCodeGenerator.GenerateProductQrCodeData(productId, productName, sku);

        // Assert
        result.Should().NotBeNullOrEmpty();
        
        var qrData = JsonSerializer.Deserialize<JsonElement>(result);
        qrData.GetProperty("Name").GetString().Should().Be(productName);
        qrData.GetProperty("Sku").GetString().Should().Be(sku);
    }

    [Fact]
    public void GenerateRentalQrCodeData_WithValidInput_ShouldReturnValidJson()
    {
        // Arrange
        var rentalId = Guid.NewGuid();
        var startDate = DateTime.UtcNow.Date;
        var endDate = startDate.AddDays(3);

        // Act
        var result = _qrCodeGenerator.GenerateRentalQrCodeData(rentalId, startDate, endDate);

        // Assert
        result.Should().NotBeNullOrEmpty();
        
        // Should be valid JSON
        var qrData = JsonSerializer.Deserialize<JsonElement>(result);
        qrData.GetProperty("Type").GetString().Should().Be("Rental");
        qrData.GetProperty("RentalId").GetString().Should().Be(rentalId.ToString());
        qrData.GetProperty("StartDate").GetString().Should().Be(startDate.ToString("O"));
        qrData.GetProperty("EndDate").GetString().Should().Be(endDate.ToString("O"));
        qrData.GetProperty("GeneratedAt").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GenerateRentalQrCodeData_WithDifferentDates_ShouldFormatCorrectly()
    {
        // Arrange
        var rentalId = Guid.NewGuid();
        var startDate = new DateTime(2024, 12, 25, 10, 30, 0, DateTimeKind.Utc);
        var endDate = new DateTime(2024, 12, 28, 18, 0, 0, DateTimeKind.Utc);

        // Act
        var result = _qrCodeGenerator.GenerateRentalQrCodeData(rentalId, startDate, endDate);

        // Assert
        var qrData = JsonSerializer.Deserialize<JsonElement>(result);
        var parsedStartDate = DateTime.Parse(qrData.GetProperty("StartDate").GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
        var parsedEndDate = DateTime.Parse(qrData.GetProperty("EndDate").GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
        
        parsedStartDate.Should().Be(startDate);
        parsedEndDate.Should().Be(endDate);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task GenerateQrCodeAsync_WithNullOrEmptyData_ShouldHandleGracefully(string? data)
    {
        // Act
        var result = await _qrCodeGenerator.GenerateQrCodeAsync(data ?? string.Empty);

        // Assert
        result.Should().NotBeNull();
        result.Should().StartWith("data:text/plain;base64,");
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task GenerateQrCodeBytesAsync_WithNullOrEmptyData_ShouldHandleGracefully(string? data)
    {
        // Act
        var result = await _qrCodeGenerator.GenerateQrCodeBytesAsync(data ?? string.Empty);

        // Assert
        result.Should().NotBeNull();
    }
}
