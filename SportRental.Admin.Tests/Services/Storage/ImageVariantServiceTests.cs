using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SportRental.Admin.Services.Storage;

namespace SportRental.Admin.Tests.Services.Storage;

public class ImageVariantServiceTests
{
    private readonly Mock<IFileStorage> _mockStorage;
    private readonly Mock<ILogger<ImageVariantService>> _mockLogger;
    private readonly ImageVariantService _service;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _productId = Guid.NewGuid();

    public ImageVariantServiceTests()
    {
        _mockStorage = new Mock<IFileStorage>();
        _mockLogger = new Mock<ILogger<ImageVariantService>>();
        
        // Setup storage to return URL based on path
        _mockStorage.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string path, byte[] _, CancellationToken _) => $"https://storage.test/{path}");

        var config = new ConfigurationBuilder().Build();
        _service = new ImageVariantService(_mockStorage.Object, config, _mockLogger.Object);
    }

    [Fact]
    public async Task SaveProductImageAsync_WithLargeImage_CreatesAllVariants()
    {
        // Arrange - create a 2000x1500 test image
        using var testImage = new Image<Rgba32>(2000, 1500);
        using var stream = new MemoryStream();
        await testImage.SaveAsJpegAsync(stream);
        stream.Position = 0;

        // Act
        var (basePath, defaultUrl, variants) = await _service.SaveProductImageAsync(
            _tenantId, _productId, "test.jpg", stream);

        // Assert
        basePath.Should().Contain(_tenantId.ToString());
        basePath.Should().Contain(_productId.ToString());
        
        defaultUrl.Should().NotBeNullOrEmpty();
        defaultUrl.Should().Contain("w800");

        // Should have variants for 400, 800, 1280
        variants.Should().ContainKey(400);
        variants.Should().ContainKey(800);
        variants.Should().ContainKey(1280);

        // Verify storage was called for original + 3 variants
        _mockStorage.Verify(s => s.SaveAsync(
            It.Is<string>(p => p.Contains("original")),
            It.IsAny<byte[]>(),
            It.IsAny<CancellationToken>()), Times.Once);

        _mockStorage.Verify(s => s.SaveAsync(
            It.Is<string>(p => p.Contains("w400")),
            It.IsAny<byte[]>(),
            It.IsAny<CancellationToken>()), Times.Once);

        _mockStorage.Verify(s => s.SaveAsync(
            It.Is<string>(p => p.Contains("w800")),
            It.IsAny<byte[]>(),
            It.IsAny<CancellationToken>()), Times.Once);

        _mockStorage.Verify(s => s.SaveAsync(
            It.Is<string>(p => p.Contains("w1280")),
            It.IsAny<byte[]>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveProductImageAsync_WithSmallImage_SkipsBiggerVariants()
    {
        // Arrange - create a 300x200 test image (smaller than 400px)
        using var testImage = new Image<Rgba32>(300, 200);
        using var stream = new MemoryStream();
        await testImage.SaveAsJpegAsync(stream);
        stream.Position = 0;

        // Act
        var (basePath, defaultUrl, variants) = await _service.SaveProductImageAsync(
            _tenantId, _productId, "small.jpg", stream);

        // Assert
        basePath.Should().NotBeNullOrEmpty();
        
        // Should still have w800 (forced) but skip others
        variants.Should().ContainKey(800); // w800 is always created as default

        // Should NOT have w1280 (original is smaller)
        variants.Should().NotContainKey(1280);
    }

    [Fact]
    public async Task SaveProductImageAsync_WithPngFile_PreservesPngFormat()
    {
        // Arrange
        using var testImage = new Image<Rgba32>(1000, 800);
        using var stream = new MemoryStream();
        await testImage.SaveAsPngAsync(stream);
        stream.Position = 0;

        // Act
        var (basePath, _, _) = await _service.SaveProductImageAsync(
            _tenantId, _productId, "test.png", stream);

        // Assert
        _mockStorage.Verify(s => s.SaveAsync(
            It.Is<string>(p => p.EndsWith(".png")),
            It.IsAny<byte[]>(),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task SaveProductImageAsync_WithWebpFile_PreservesWebpFormat()
    {
        // Arrange
        using var testImage = new Image<Rgba32>(1000, 800);
        using var stream = new MemoryStream();
        await testImage.SaveAsWebpAsync(stream);
        stream.Position = 0;

        // Act
        var (basePath, _, _) = await _service.SaveProductImageAsync(
            _tenantId, _productId, "test.webp", stream);

        // Assert
        _mockStorage.Verify(s => s.SaveAsync(
            It.Is<string>(p => p.EndsWith(".webp")),
            It.IsAny<byte[]>(),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task SaveProductImageAsync_ReturnsCorrectBasePath()
    {
        // Arrange
        using var testImage = new Image<Rgba32>(800, 600);
        using var stream = new MemoryStream();
        await testImage.SaveAsJpegAsync(stream);
        stream.Position = 0;

        // Act
        var (basePath, _, _) = await _service.SaveProductImageAsync(
            _tenantId, _productId, "test.jpg", stream);

        // Assert
        basePath.Should().Be($"images/products/{_tenantId}/{_productId}/v1");
    }

    [Fact]
    public async Task SaveProductImageAsync_VariantUrlsAreValid()
    {
        // Arrange
        using var testImage = new Image<Rgba32>(1500, 1000);
        using var stream = new MemoryStream();
        await testImage.SaveAsJpegAsync(stream);
        stream.Position = 0;

        // Act
        var (_, defaultUrl, variants) = await _service.SaveProductImageAsync(
            _tenantId, _productId, "test.jpg", stream);

        // Assert
        defaultUrl.Should().StartWith("https://");
        
        foreach (var (width, url) in variants)
        {
            url.Should().StartWith("https://");
            url.Should().Contain($"w{width}");
        }
    }

    [Fact]
    public async Task SaveProductImageAsync_MaintainsAspectRatio()
    {
        // Arrange - wide image 1600x800 (2:1 ratio)
        using var testImage = new Image<Rgba32>(1600, 800);
        using var stream = new MemoryStream();
        await testImage.SaveAsJpegAsync(stream);
        stream.Position = 0;

        byte[]? savedBytes800 = null;
        _mockStorage.Setup(s => s.SaveAsync(
            It.Is<string>(p => p.Contains("w800")),
            It.IsAny<byte[]>(),
            It.IsAny<CancellationToken>()))
            .Callback<string, byte[], CancellationToken>((_, bytes, _) => savedBytes800 = bytes)
            .ReturnsAsync("https://test/w800.jpg");

        // Act
        await _service.SaveProductImageAsync(_tenantId, _productId, "wide.jpg", stream);

        // Assert - verify the resized image maintains aspect ratio
        savedBytes800.Should().NotBeNull();
        using var resizedImage = Image.Load(savedBytes800!);
        
        // Width should be 800, height should be ~400 (maintaining 2:1 ratio)
        resizedImage.Width.Should().Be(800);
        resizedImage.Height.Should().BeInRange(395, 405); // Allow small rounding difference
    }
}

