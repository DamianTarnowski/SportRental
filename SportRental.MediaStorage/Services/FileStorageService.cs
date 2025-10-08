using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SportRental.MediaStorage.Data;
using SportRental.MediaStorage.Models;
using SportRental.MediaStorage.Options;

namespace SportRental.MediaStorage.Services;

public class FileStorageService
{
    private readonly MediaStorageDbContext _db;
    private readonly StorageOptions _storageOptions;

    public FileStorageService(MediaStorageDbContext db, IOptions<StorageOptions> storageOptions)
    {
        _db = db;
        _storageOptions = storageOptions.Value;
        Directory.CreateDirectory(_storageOptions.RootPath);
    }

    public async Task<StoredFile> SaveAsync(Guid tenantId, IFormFile file, string? requestedPath = null, CancellationToken cancellationToken = default)
    {
        ValidateFile(file);

        var (relativePath, destinationPath, storedFileName) = PreparePaths(tenantId, requestedPath, file.FileName);

        await using var stream = new FileStream(destinationPath, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
        await file.CopyToAsync(stream, cancellationToken);

        stream.Position = 0;
        string sha256;
        using (var sha = SHA256.Create())
        {
            var hash = await sha.ComputeHashAsync(stream, cancellationToken);
            sha256 = Convert.ToHexString(hash);
        }

        var entity = new StoredFile
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OriginalFileName = file.FileName,
            StoredFileName = storedFileName,
            ContentType = file.ContentType,
            Size = file.Length,
            RelativePath = relativePath,
            Sha256 = sha256,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.Files.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public async Task<StoredFile?> FindAsync(Guid id, CancellationToken cancellationToken = default)
        => await _db.Files.FirstOrDefaultAsync(f => f.Id == id, cancellationToken);

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _db.Files.FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (entity is null)
        {
            return false;
        }

        var fullPath = GetAbsolutePath(entity.RelativePath);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        _db.Files.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public string BuildPublicUrl(StoredFile file, HttpRequest request)
    {
        if (!string.IsNullOrWhiteSpace(_storageOptions.PublicBaseUrl))
        {
            return $"{_storageOptions.PublicBaseUrl.TrimEnd('/')}/files/{file.RelativePath}";
        }

        var scheme = request.Scheme;
        var host = request.Host.Value;
        return $"{scheme}://{host}/files/{file.RelativePath}";
    }

    public string GetAbsolutePath(string relativePath)
    {
        var normalized = NormalizeRelativePath(relativePath);
        return Path.Combine(_storageOptions.RootPath, normalized.Replace('/', Path.DirectorySeparatorChar));
    }

    public static string NormalizeRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("Relative path cannot be empty", nameof(relativePath));
        }

        var cleaned = relativePath.Replace("\\", "/").Trim('/');
        if (cleaned.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Relative path cannot contain parent directory segments.");
        }

        return cleaned;
    }

    private (string relativePath, string destinationPath, string storedFileName) PreparePaths(Guid tenantId, string? requestedPath, string originalFileName)
    {
        string relativePath;
        if (!string.IsNullOrWhiteSpace(requestedPath))
        {
            var normalized = NormalizeRelativePath(requestedPath);
            if (!TryExtractTenantId(normalized, out _))
            {
                normalized = NormalizeRelativePath(Path.Combine(tenantId.ToString(), normalized));
            }
            relativePath = normalized;
        }
        else
        {
            var extension = Path.GetExtension(originalFileName);
            relativePath = NormalizeRelativePath(Path.Combine(tenantId.ToString(), $"{Guid.NewGuid()}{extension}"));
        }

        var storedFileName = Path.GetFileName(relativePath);
        var directory = Path.GetDirectoryName(relativePath);
        var tenantDirectory = directory is null
            ? _storageOptions.RootPath
            : Path.Combine(_storageOptions.RootPath, directory.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(tenantDirectory);
        var destinationPath = Path.Combine(tenantDirectory, storedFileName);
        return (relativePath, destinationPath, storedFileName);
    }

    private static bool TryExtractTenantId(string normalizedPath, out Guid tenantId)
    {
        var segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            if (Guid.TryParse(segment, out tenantId))
            {
                return true;
            }
        }

        tenantId = Guid.Empty;
        return false;
    }

    private void ValidateFile(IFormFile file)
    {
        if (file.Length == 0)
        {
            throw new InvalidOperationException("File is empty.");
        }

        if (file.Length > _storageOptions.MaxFileSizeBytes)
        {
            throw new InvalidOperationException($"File exceeds allowed size {_storageOptions.MaxFileSizeBytes} bytes.");
        }

        var extension = Path.GetExtension(file.FileName).TrimStart('.').ToLowerInvariant();
        if (_storageOptions.AllowedExtensions.Length > 0 && !_storageOptions.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("File extension is not allowed.");
        }
    }
}

