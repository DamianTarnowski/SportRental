using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Configuration;

namespace SportRental.Admin.Services.Storage;

/// <summary>
/// Azure Blob Storage implementation for production use.
/// Uses two containers: a public one for product images/logos and a private one
/// (no anonymous access) for sensitive artefacts such as PDF contracts.
/// </summary>
public sealed class AzureBlobStorage : IFileStorage
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly string _containerName;
    private readonly string _privateContainerName;
    private readonly string? _publicBaseUrl;
    private readonly ILogger<AzureBlobStorage> _logger;

    public AzureBlobStorage(IConfiguration config, ILogger<AzureBlobStorage> logger)
    {
        _logger = logger;

        var connectionString = config["Storage:AzureBlob:ConnectionString"];
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Storage:AzureBlob:ConnectionString is required for AzureBlobStorage");
        }

        _blobServiceClient = new BlobServiceClient(connectionString);
        _containerName = config["Storage:AzureBlob:ContainerName"] ?? "images";
        _privateContainerName = config["Storage:AzureBlob:PrivateContainerName"] ?? $"{_containerName}-private";
        _publicBaseUrl = config["Storage:AzureBlob:PublicBaseUrl"];

        _logger.LogInformation(
            "AzureBlobStorage initialized. Public container: {Public}, private container: {Private}",
            _containerName, _privateContainerName);
    }

    public async Task<string> SaveAsync(string relativePath, byte[] content, CancellationToken ct = default)
    {
        using var ms = new MemoryStream(content, writable: false);
        return await SaveAsync(relativePath, ms, ct);
    }

    public async Task<string> SaveAsync(string relativePath, Stream content, CancellationToken ct = default)
    {
        try
        {
            var normalized = NormalizeRelativePath(relativePath);

            var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
            await containerClient.CreateIfNotExistsAsync(PublicAccessType.Blob, cancellationToken: ct);

            var blobClient = containerClient.GetBlobClient(normalized);
            var blobHttpHeaders = new BlobHttpHeaders
            {
                ContentType = GetContentType(normalized),
                CacheControl = "public, max-age=31536000"
            };

            await blobClient.UploadAsync(
                content,
                new BlobUploadOptions { HttpHeaders = blobHttpHeaders },
                cancellationToken: ct);

            _logger.LogInformation("Uploaded public blob: {BlobName}", normalized);

            if (!string.IsNullOrWhiteSpace(_publicBaseUrl))
            {
                return $"{_publicBaseUrl.TrimEnd('/')}/{normalized}";
            }

            return blobClient.Uri.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload blob: {Path}", relativePath);
            throw;
        }
    }

    public async Task<string> SavePrivateAsync(string relativePath, byte[] content, CancellationToken ct = default)
    {
        try
        {
            var normalized = NormalizeRelativePath(relativePath);

            var containerClient = _blobServiceClient.GetBlobContainerClient(_privateContainerName);
            await containerClient.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);

            var blobClient = containerClient.GetBlobClient(normalized);
            var blobHttpHeaders = new BlobHttpHeaders
            {
                ContentType = GetContentType(normalized),
                CacheControl = "private, no-store"
            };

            using var ms = new MemoryStream(content, writable: false);
            await blobClient.UploadAsync(
                ms,
                new BlobUploadOptions { HttpHeaders = blobHttpHeaders },
                cancellationToken: ct);

            _logger.LogInformation("Uploaded private blob: {BlobName}", normalized);

            return normalized;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload private blob: {Path}", relativePath);
            throw;
        }
    }

    public Task<string> GetPrivateReadUrlAsync(string storageReference, TimeSpan ttl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(storageReference))
            throw new ArgumentException("Storage reference is empty", nameof(storageReference));

        var (containerName, blobName) = ResolvePrivateReference(storageReference);
        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        var blobClient = containerClient.GetBlobClient(blobName);

        if (!blobClient.CanGenerateSasUri)
        {
            throw new InvalidOperationException(
                "Azure Blob client cannot generate SAS URIs. Ensure the connection string contains an account key.");
        }

        var sasBuilder = new BlobSasBuilder
        {
            BlobContainerName = containerName,
            BlobName = blobName,
            Resource = "b",
            ExpiresOn = DateTimeOffset.UtcNow.Add(ttl)
        };
        sasBuilder.SetPermissions(BlobSasPermissions.Read);

        var sasUri = blobClient.GenerateSasUri(sasBuilder);
        return Task.FromResult(sasUri.ToString());
    }

    private (string Container, string Blob) ResolvePrivateReference(string storageReference)
    {
        if (storageReference.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || storageReference.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            var uri = new Uri(storageReference);
            var segments = uri.AbsolutePath.TrimStart('/').Split('/', 2);
            if (segments.Length != 2)
                throw new ArgumentException("Cannot parse blob URL: missing container/blob segments", nameof(storageReference));
            return (segments[0], segments[1]);
        }

        return (_privateContainerName, NormalizeRelativePath(storageReference));
    }

    public async Task<byte[]> ReadAsync(string relativePath, CancellationToken ct = default)
    {
        try
        {
            var normalized = NormalizeRelativePath(relativePath);
            var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
            var blobClient = containerClient.GetBlobClient(normalized);

            using var ms = new MemoryStream();
            await blobClient.DownloadToAsync(ms, ct);
            return ms.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read blob: {Path}", relativePath);
            throw;
        }
    }

    public async Task<bool> ExistsAsync(string relativePath, CancellationToken ct = default)
    {
        try
        {
            var normalized = NormalizeRelativePath(relativePath);
            var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
            var blobClient = containerClient.GetBlobClient(normalized);

            return await blobClient.ExistsAsync(ct);
        }
        catch
        {
            return false;
        }
    }

    public async Task DeleteAsync(string relativePath, CancellationToken ct = default)
    {
        try
        {
            var normalized = NormalizeRelativePath(relativePath);
            var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
            var blobClient = containerClient.GetBlobClient(normalized);

            await blobClient.DeleteIfExistsAsync(cancellationToken: ct);
            _logger.LogInformation("Deleted blob: {BlobName}", normalized);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete blob: {Path}", relativePath);
            throw;
        }
    }

    private static string NormalizeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path cannot be null or empty", nameof(path));

        var normalized = path.Replace('\\', '/').TrimStart('/');

        foreach (var segment in normalized.Split('/'))
        {
            if (segment.Length == 0)
                throw new ArgumentException("Path contains empty segments", nameof(path));
            if (segment == "." || segment == "..")
                throw new ArgumentException("Path traversal segments are not allowed", nameof(path));
        }

        return normalized;
    }

    private static string GetContentType(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            ".bmp" => "image/bmp",
            ".ico" => "image/x-icon",
            ".pdf" => "application/pdf",
            ".zip" => "application/zip",
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".txt" => "text/plain",
            ".css" => "text/css",
            ".js" => "application/javascript",
            ".html" or ".htm" => "text/html",
            _ => "application/octet-stream"
        };
    }
}
