using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Configuration;

namespace SportRental.Admin.Services.Storage;

/// <summary>
/// Azure Blob Storage implementation for production use
/// Supports Azure Storage Account with automatic container creation
/// </summary>
public sealed class AzureBlobStorage : IFileStorage
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly string _containerName;
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
        _publicBaseUrl = config["Storage:AzureBlob:PublicBaseUrl"]; // Optional: for custom CDN domain
        
        _logger.LogInformation("AzureBlobStorage initialized. Container: {Container}", _containerName);
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
            
            // Get or create container
            var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
            await containerClient.CreateIfNotExistsAsync(PublicAccessType.Blob, cancellationToken: ct);
            
            // Upload blob
            var blobClient = containerClient.GetBlobClient(normalized);
            
            // Set content type based on file extension
            var contentType = GetContentType(normalized);
            var blobHttpHeaders = new BlobHttpHeaders
            {
                ContentType = contentType,
                CacheControl = "public, max-age=31536000" // 1 year cache for immutable images
            };

            await blobClient.UploadAsync(
                content, 
                new BlobUploadOptions
                {
                    HttpHeaders = blobHttpHeaders,
                    Conditions = null // Overwrite if exists
                },
                cancellationToken: ct);

            _logger.LogInformation("Uploaded blob: {BlobName}, Size: {Size} bytes", normalized, content.Length);

            // Return public URL
            if (!string.IsNullOrWhiteSpace(_publicBaseUrl))
            {
                // Use custom CDN URL if configured
                return $"{_publicBaseUrl.TrimEnd('/')}/{normalized}";
            }
            
            // Return Azure Blob URL
            return blobClient.Uri.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload blob: {Path}", relativePath);
            throw;
        }
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

        // Remove leading slash if present
        path = path.TrimStart('/');
        
        // Normalize path separators to forward slash (Azure Blob uses forward slash)
        path = path.Replace('\\', '/');
        
        return path;
    }

    private static Guid? ExtractTenantId(string path)
    {
        // Try to extract tenant ID from path like "images/products/{tenantId}/..."
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 2 && Guid.TryParse(parts[2], out var tenantId))
        {
            return tenantId;
        }
        return null;
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
