using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Moq;
using SportRental.Admin.Services.Storage;

namespace SportRental.Admin.Tests.Services.Storage;

public class LocalFileStorageTests : IDisposable
{
    private readonly string _testDir;
    private readonly LocalFileStorage _storage;
    private readonly Mock<IWebHostEnvironment> _mockEnv;

    public LocalFileStorageTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"LocalFileStorageTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDir);

        _mockEnv = new Mock<IWebHostEnvironment>();
        _mockEnv.Setup(e => e.WebRootPath).Returns(_testDir);

        _storage = new LocalFileStorage(_mockEnv.Object);
    }

    [Fact]
    public async Task SaveAsync_WithBytes_CreatesFileAndReturnsUrl()
    {
        // Arrange
        var content = new byte[] { 1, 2, 3, 4, 5 };
        var relativePath = "test/file.bin";

        // Act
        var url = await _storage.SaveAsync(relativePath, content);

        // Assert
        url.Should().Be("/test/file.bin");
        
        var fullPath = Path.Combine(_testDir, "test", "file.bin");
        File.Exists(fullPath).Should().BeTrue();
        
        var savedContent = await File.ReadAllBytesAsync(fullPath);
        savedContent.Should().BeEquivalentTo(content);
    }

    [Fact]
    public async Task SaveAsync_WithStream_CreatesFileAndReturnsUrl()
    {
        // Arrange
        var content = new byte[] { 10, 20, 30, 40, 50 };
        using var stream = new MemoryStream(content);
        var relativePath = "uploads/document.pdf";

        // Act
        var url = await _storage.SaveAsync(relativePath, stream);

        // Assert
        url.Should().Be("/uploads/document.pdf");
        
        var fullPath = Path.Combine(_testDir, "uploads", "document.pdf");
        File.Exists(fullPath).Should().BeTrue();
        
        var savedContent = await File.ReadAllBytesAsync(fullPath);
        savedContent.Should().BeEquivalentTo(content);
    }

    [Fact]
    public async Task SaveAsync_CreatesNestedDirectories()
    {
        // Arrange
        var content = new byte[] { 1 };
        var relativePath = "deep/nested/path/to/file.txt";

        // Act
        var url = await _storage.SaveAsync(relativePath, content);

        // Assert
        var fullPath = Path.Combine(_testDir, "deep", "nested", "path", "to", "file.txt");
        File.Exists(fullPath).Should().BeTrue();
    }

    [Fact]
    public async Task ReadAsync_ReturnsFileContent()
    {
        // Arrange
        var expectedContent = new byte[] { 100, 200, 255 };
        var relativePath = "readable/data.bin";
        
        var dir = Path.Combine(_testDir, "readable");
        Directory.CreateDirectory(dir);
        await File.WriteAllBytesAsync(Path.Combine(dir, "data.bin"), expectedContent);

        // Act
        var content = await _storage.ReadAsync(relativePath);

        // Assert
        content.Should().BeEquivalentTo(expectedContent);
    }

    [Fact]
    public async Task ReadAsync_WhenFileNotExists_ThrowsException()
    {
        // Arrange
        var relativePath = "nonexistent/file.bin";

        // Act & Assert
        // Can throw either FileNotFoundException or DirectoryNotFoundException
        await _storage.Invoking(s => s.ReadAsync(relativePath))
            .Should().ThrowAsync<IOException>();
    }

    [Fact]
    public async Task ExistsAsync_WhenFileExists_ReturnsTrue()
    {
        // Arrange
        var relativePath = "existing/file.txt";
        var dir = Path.Combine(_testDir, "existing");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "file.txt"), "test");

        // Act
        var exists = await _storage.ExistsAsync(relativePath);

        // Assert
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_WhenFileNotExists_ReturnsFalse()
    {
        // Arrange
        var relativePath = "missing/file.txt";

        // Act
        var exists = await _storage.ExistsAsync(relativePath);

        // Assert
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task SaveAsync_OverwritesExistingFile()
    {
        // Arrange
        var relativePath = "overwrite/file.bin";
        var originalContent = new byte[] { 1, 2, 3 };
        var newContent = new byte[] { 4, 5, 6, 7, 8 };

        // Act
        await _storage.SaveAsync(relativePath, originalContent);
        await _storage.SaveAsync(relativePath, newContent);

        // Assert
        var fullPath = Path.Combine(_testDir, "overwrite", "file.bin");
        var savedContent = await File.ReadAllBytesAsync(fullPath);
        savedContent.Should().BeEquivalentTo(newContent);
    }

    [Fact]
    public async Task SaveAsync_HandlesPathWithBackslashes()
    {
        // Arrange
        var content = new byte[] { 1 };
        var relativePath = @"path\with\backslashes\file.txt";

        // Act
        var url = await _storage.SaveAsync(relativePath, content);

        // Assert
        url.Should().Be("/path/with/backslashes/file.txt");
        
        var fullPath = Path.Combine(_testDir, "path", "with", "backslashes", "file.txt");
        File.Exists(fullPath).Should().BeTrue();
    }

    [Fact]
    public async Task SaveAsync_WithLargeFile_Works()
    {
        // Arrange - 1MB file
        var content = new byte[1024 * 1024];
        new Random().NextBytes(content);
        var relativePath = "large/bigfile.bin";

        // Act
        var url = await _storage.SaveAsync(relativePath, content);

        // Assert
        var fullPath = Path.Combine(_testDir, "large", "bigfile.bin");
        var savedContent = await File.ReadAllBytesAsync(fullPath);
        savedContent.Should().HaveCount(1024 * 1024);
        savedContent.Should().BeEquivalentTo(content);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}

