using SportRental.Admin.Services.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;
using FluentAssertions;

namespace SportRental.Admin.Tests.Services;

/// <summary>
/// Integration tests for Azure Blob Storage
/// These tests require actual Azure Storage Account connection
/// </summary>
public class AzureBlobStorageTests : IDisposable
{
    private readonly AzureBlobStorage _storage;
    private readonly List<string> _uploadedFiles = new();

    public AzureBlobStorageTests()
    {
        // Load configuration from appsettings.Test.json or environment
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.Test.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var logger = new LoggerFactory().CreateLogger<AzureBlobStorage>();
        
        _storage = new AzureBlobStorage(configuration, logger);
    }

    [Fact]
    public async Task SaveAsync_ShouldUploadImageToAzureBlob()
    {
        // Arrange
        var testImagePath = $"test/images/test-image-{Guid.NewGuid()}.jpg";
        var testImageContent = GenerateTestImage();

        // Act
        var url = await _storage.SaveAsync(testImagePath, testImageContent);
        _uploadedFiles.Add(testImagePath);

        // Assert
        url.Should().NotBeNullOrEmpty();
        url.Should().Contain("blob.core.windows.net", "URL should point to Azure Blob Storage");
        url.Should().Contain("srblob", "URL should contain container name");
        url.Should().Contain(testImagePath, "URL should contain the file path");
    }

    [Fact]
    public async Task SaveAsync_AndReadAsync_ShouldRoundTrip()
    {
        // Arrange
        var testPath = $"test/roundtrip/image-{Guid.NewGuid()}.png";
        var originalContent = GenerateTestImage(1024); // 1KB test image

        // Act - Upload
        var uploadUrl = await _storage.SaveAsync(testPath, originalContent);
        _uploadedFiles.Add(testPath);

        // Act - Download
        var downloadedContent = await _storage.ReadAsync(testPath);

        // Assert
        uploadUrl.Should().NotBeNullOrEmpty();
        downloadedContent.Should().NotBeNull();
        downloadedContent.Length.Should().Be(originalContent.Length, "Downloaded content should match uploaded size");
        downloadedContent.Should().BeEquivalentTo(originalContent, "Content should match exactly");
    }

    [Fact]
    public async Task ExistsAsync_AfterUpload_ShouldReturnTrue()
    {
        // Arrange
        var testPath = $"test/exists/file-{Guid.NewGuid()}.txt";
        var content = System.Text.Encoding.UTF8.GetBytes("Test content for exists check");

        // Act - Upload
        await _storage.SaveAsync(testPath, content);
        _uploadedFiles.Add(testPath);

        // Act - Check exists
        var exists = await _storage.ExistsAsync(testPath);

        // Assert
        exists.Should().BeTrue("File should exist after upload");
    }

    [Fact]
    public async Task ExistsAsync_NonExistentFile_ShouldReturnFalse()
    {
        // Arrange
        var nonExistentPath = $"test/nonexistent/file-{Guid.NewGuid()}.jpg";

        // Act
        var exists = await _storage.ExistsAsync(nonExistentPath);

        // Assert
        exists.Should().BeFalse("Non-existent file should return false");
    }

    [Fact]
    public async Task SaveAsync_MultipleImages_ShouldAllSucceed()
    {
        // Arrange
        var imageCount = 3;
        var tasks = new List<Task<string>>();

        // Act - Upload multiple images concurrently
        for (int i = 0; i < imageCount; i++)
        {
            var path = $"test/multiple/image-{i}-{Guid.NewGuid()}.jpg";
            var content = GenerateTestImage(512 * (i + 1)); // Different sizes
            tasks.Add(_storage.SaveAsync(path, content));
            _uploadedFiles.Add(path);
        }

        var urls = await Task.WhenAll(tasks);

        // Assert
        urls.Should().HaveCount(imageCount);
        urls.Should().OnlyContain(url => !string.IsNullOrEmpty(url));
        urls.Should().OnlyContain(url => url.Contains("blob.core.windows.net"));
        urls.Distinct().Should().HaveCount(imageCount, "All URLs should be unique");
    }

    [Fact]
    public async Task SaveAsync_WithTenantPath_ShouldOrganizeCorrectly()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var imagePath = $"images/products/{tenantId}/{productId}/v1/w800.jpg";
        var content = GenerateTestImage();

        // Act
        var url = await _storage.SaveAsync(imagePath, content);
        _uploadedFiles.Add(imagePath);

        // Assert
        url.Should().Contain(tenantId.ToString(), "URL should contain tenant ID");
        url.Should().Contain(productId.ToString(), "URL should contain product ID");
        url.Should().EndWith("w800.jpg", "URL should end with filename");
    }

    [Fact]
    public async Task SaveAsync_LargeImage_ShouldSucceed()
    {
        // Arrange - 2MB image
        var largePath = $"test/large/image-{Guid.NewGuid()}.jpg";
        var largeContent = GenerateTestImage(2 * 1024 * 1024); // 2MB

        // Act
        var url = await _storage.SaveAsync(largePath, largeContent);
        _uploadedFiles.Add(largePath);

        // Assert
        url.Should().NotBeNullOrEmpty();
        
        // Verify we can read it back
        var downloaded = await _storage.ReadAsync(largePath);
        downloaded.Length.Should().Be(largeContent.Length);
    }

    [Fact]
    public async Task DeleteAsync_ExistingFile_ShouldRemove()
    {
        // Arrange
        var testPath = $"test/delete/file-{Guid.NewGuid()}.txt";
        var content = System.Text.Encoding.UTF8.GetBytes("To be deleted");
        
        await _storage.SaveAsync(testPath, content);
        var existsBefore = await _storage.ExistsAsync(testPath);

        // Act
        await _storage.DeleteAsync(testPath);

        // Act - Check after delete
        var existsAfter = await _storage.ExistsAsync(testPath);

        // Assert
        existsBefore.Should().BeTrue("File should exist before deletion");
        existsAfter.Should().BeFalse("File should not exist after deletion");
    }

    [Fact]
    public async Task SaveAsync_WithStream_ShouldWork()
    {
        // Arrange
        var testPath = $"test/stream/image-{Guid.NewGuid()}.jpg";
        var content = GenerateTestImage();
        using var stream = new MemoryStream(content);

        // Act
        var url = await _storage.SaveAsync(testPath, stream);
        _uploadedFiles.Add(testPath);

        // Assert
        url.Should().NotBeNullOrEmpty();
        
        // Verify content
        var downloaded = await _storage.ReadAsync(testPath);
        downloaded.Should().BeEquivalentTo(content);
    }

    [Fact]
    public async Task SaveAsync_OverwriteExisting_ShouldSucceed()
    {
        // Arrange
        var testPath = $"test/overwrite/file-{Guid.NewGuid()}.txt";
        var content1 = System.Text.Encoding.UTF8.GetBytes("Version 1");
        var content2 = System.Text.Encoding.UTF8.GetBytes("Version 2 - Updated");

        // Act - First upload
        var url1 = await _storage.SaveAsync(testPath, content1);
        _uploadedFiles.Add(testPath);

        // Act - Overwrite
        var url2 = await _storage.SaveAsync(testPath, content2);

        // Act - Read back
        var downloaded = await _storage.ReadAsync(testPath);

        // Assert
        url1.Should().NotBeNullOrEmpty();
        url2.Should().NotBeNullOrEmpty();
        url1.Should().Be(url2, "URL should be the same for overwritten file");
        
        var downloadedText = System.Text.Encoding.UTF8.GetString(downloaded);
        downloadedText.Should().Be("Version 2 - Updated", "Content should be from second upload");
    }

    /// <summary>
    /// Generates a test image with random bytes
    /// </summary>
    private byte[] GenerateTestImage(int size = 1024)
    {
        var random = new Random();
        var bytes = new byte[size];
        random.NextBytes(bytes);
        
        // Add JPEG header for realism (not required for test, but nice to have)
        if (size >= 10)
        {
            bytes[0] = 0xFF;
            bytes[1] = 0xD8; // JPEG SOI marker
            bytes[size - 2] = 0xFF;
            bytes[size - 1] = 0xD9; // JPEG EOI marker
        }
        
        return bytes;
    }

    /// <summary>
    /// Cleanup - delete all uploaded test files
    /// </summary>
    public void Dispose()
    {
        // Cleanup test files
        foreach (var filePath in _uploadedFiles)
        {
            try
            {
                _storage.DeleteAsync(filePath).Wait();
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}

/// <summary>
/// Manual integration test - requires real Azure credentials
/// Run with: dotnet test --filter "Category=Integration"
/// </summary>
[Trait("Category", "Integration")]
public class AzureBlobStorageManualTests
{
    [Fact]
    public async Task FullWorkflow_UploadProductImage_ShouldWork()
    {
        // This test simulates the full product image upload workflow
        
        // Arrange
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.Test.json")
            .Build();

        var logger = new LoggerFactory().CreateLogger<AzureBlobStorage>();
        var storage = new AzureBlobStorage(configuration, logger);

        var tenantId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        
        // Simulate ImageVariantService behavior
        var basePath = $"images/products/{tenantId}/{productId}/v1";
        var originalPath = $"{basePath}/original.jpg";
        var w800Path = $"{basePath}/w800.jpg";
        
        // Generate test images
        var originalImage = new byte[2 * 1024 * 1024]; // 2MB
        var w800Image = new byte[500 * 1024]; // 500KB
        new Random().NextBytes(originalImage);
        new Random().NextBytes(w800Image);

        // Act - Upload both variants
        var originalUrl = await storage.SaveAsync(originalPath, originalImage);
        var w800Url = await storage.SaveAsync(w800Path, w800Image);

        // Assert
        originalUrl.Should().NotBeNullOrEmpty();
        w800Url.Should().NotBeNullOrEmpty();
        
        originalUrl.Should().Contain("original.jpg");
        w800Url.Should().Contain("w800.jpg");
        
        // Verify both exist
        (await storage.ExistsAsync(originalPath)).Should().BeTrue();
        (await storage.ExistsAsync(w800Path)).Should().BeTrue();
        
        // Verify URLs are accessible
        originalUrl.Should().StartWith("https://");
        w800Url.Should().StartWith("https://");
        
        // Console output is not available in async tests, use debug output instead
        System.Diagnostics.Debug.WriteLine($"✅ Original: {originalUrl}");
        System.Diagnostics.Debug.WriteLine($"✅ W800: {w800Url}");
        
        // Cleanup
        await storage.DeleteAsync(originalPath);
        await storage.DeleteAsync(w800Path);
    }
}
