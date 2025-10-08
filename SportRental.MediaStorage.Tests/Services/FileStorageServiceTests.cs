using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SportRental.MediaStorage.Data;
using SportRental.MediaStorage.Models;
using SportRental.MediaStorage.Options;
using SportRental.MediaStorage.Services;
using Xunit;

namespace SportRental.MediaStorage.Tests.Services;

public class FileStorageServiceTests : IAsyncLifetime
{
    private readonly string _tempRoot;
    private readonly MediaStorageDbContext _db;
    private readonly FileStorageService _service;

    public FileStorageServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "MediaStorageTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempRoot);

        var options = new DbContextOptionsBuilder<MediaStorageDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_tempRoot, "media-tests.db")}")
            .Options;
        _db = new MediaStorageDbContext(options);
        _db.Database.EnsureCreated();

        var storageOptions = Microsoft.Extensions.Options.Options.Create(new StorageOptions
        {
            RootPath = _tempRoot,
            AllowedExtensions = new[] { "jpg", "png", "pdf" },
            MaxFileSizeBytes = 5 * 1024 * 1024,
            PublicBaseUrl = "https://cdn.test"
        });

        _service = new FileStorageService(_db, storageOptions);
    }

    [Fact]
    public async Task SaveAsync_PersistsFileAndMetadata()
    {
        var tenantId = Guid.NewGuid();
        var content = new byte[] { 1, 2, 3 };
        await using var stream = new MemoryStream(content);

        var stored = await _service.SaveAsync(tenantId, new FormFile(stream, 0, content.Length, "file", "logo.png")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/png"
        }, cancellationToken: CancellationToken.None);

        stored.TenantId.Should().Be(tenantId);
        stored.StoredFileName.Should().EndWith(".png");
        stored.RelativePath.Should().Contain(tenantId.ToString());
        stored.Sha256.Should().NotBeNullOrEmpty();

        var filePath = _service.GetAbsolutePath(stored.RelativePath);
        File.Exists(filePath).Should().BeTrue();
        var bytes = await File.ReadAllBytesAsync(filePath);
        bytes.Should().Equal(content);
    }

    [Fact]
    public async Task DeleteAsync_RemovesMetadataAndFile()
    {
        var tenantId = Guid.NewGuid();
        var content = new byte[] { 9, 9, 9 };
        await using var stream = new MemoryStream(content);

        var stored = await _service.SaveAsync(tenantId, new FormFile(stream, 0, content.Length, "file", "doc.pdf")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf"
        }, cancellationToken: CancellationToken.None);

        var result = await _service.DeleteAsync(stored.Id, CancellationToken.None);
        result.Should().BeTrue();
        File.Exists(_service.GetAbsolutePath(stored.RelativePath)).Should().BeFalse();
    }

    [Fact]
    public async Task SaveAsync_ThrowsForEmptyFiles()
    {
        await using var stream = new MemoryStream(Array.Empty<byte>());
        var formFile = new FormFile(stream, 0, 0, "file", "empty.png")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/png"
        };

        await FluentActions.Invoking(() => _service.SaveAsync(Guid.NewGuid(), formFile, cancellationToken: CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("File is empty.*");
    }

    [Fact]
    public async Task SaveAsync_ThrowsForDisallowedExtension()
    {
        await using var stream = new MemoryStream(new byte[] { 1 });
        var formFile = new FormFile(stream, 0, 1, "file", "malware.exe")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/octet-stream"
        };

        await FluentActions.Invoking(() => _service.SaveAsync(Guid.NewGuid(), formFile, cancellationToken: CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("File extension is not allowed.");
    }

    [Fact]
    public void NormalizeRelativePath_WithParentSegments_Throws()
    {
        FluentActions.Invoking(() => FileStorageService.NormalizeRelativePath("tenant/../secret"))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("Relative path cannot contain parent directory segments.");
    }

    public async Task InitializeAsync() => await Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _db.Database.EnsureDeletedAsync();
        _db.Dispose();
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }
}
