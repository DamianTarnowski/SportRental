using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;

namespace SportRental.Admin.Services.Storage;

public sealed class RemoteFileStorage : IFileStorage
{
    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;

    private record UploadResponse(Guid Id, string DownloadUrl, string RelativePath);

    public RemoteFileStorage(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        var baseUrl = configuration["MediaStorage:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException("MediaStorage:BaseUrl configuration is required for RemoteFileStorage.");
        }

        if (_httpClient.BaseAddress is null)
        {
            _httpClient.BaseAddress = new Uri(baseUrl);
        }

        _apiKey = configuration["MediaStorage:ApiKey"];
    }

    public async Task<string> SaveAsync(string relativePath, byte[] content, CancellationToken ct = default)
    {
        using var ms = new MemoryStream(content, writable: false);
        return await SaveAsync(relativePath, ms, ct);
    }

    public async Task<string> SaveAsync(string relativePath, Stream content, CancellationToken ct = default)
    {
        var normalized = NormalizeRelativePath(relativePath);
        var tenantId = ExtractTenantId(normalized) ?? throw new InvalidOperationException("Relative path must contain tenant identifier.");

        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        buffer.Position = 0;

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/files");
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(tenantId.ToString()), "tenantId");
        form.Add(new StringContent(normalized), "path");
        var streamContent = new StreamContent(buffer);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(streamContent, "file", Path.GetFileName(normalized));
        request.Content = form;
        AddAuthHeader(request);

        using var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Media storage upload failed ({response.StatusCode}): {error}");
        }

        var result = await response.Content.ReadFromJsonAsync<UploadResponse>(cancellationToken: ct);
        if (result is null || string.IsNullOrWhiteSpace(result.DownloadUrl))
        {
            throw new InvalidOperationException("Media storage upload returned invalid payload.");
        }

        return result.DownloadUrl;
    }

    public async Task<byte[]> ReadAsync(string relativePath, CancellationToken ct = default)
    {
        var normalized = NormalizeRelativePath(relativePath);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/files/{normalized}");
        using var response = await _httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Unable to read file {normalized}: {response.StatusCode}");
        }

        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    public async Task<bool> ExistsAsync(string relativePath, CancellationToken ct = default)
    {
        try
        {
            var normalized = NormalizeRelativePath(relativePath);
            using var request = new HttpRequestMessage(HttpMethod.Head, $"/files/{normalized}");
            using var response = await _httpClient.SendAsync(request, ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private void AddAuthHeader(HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            request.Headers.TryAddWithoutValidation("X-Api-Key", _apiKey);
        }
    }

    private static string NormalizeRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("Relative path cannot be empty", nameof(relativePath));
        }

        var cleaned = relativePath
            .Replace("\\", "/")
            .Replace(Path.DirectorySeparatorChar.ToString(), "/")
            .Trim('/');

        if (cleaned.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Relative path cannot contain parent directory segments.");
        }

        return cleaned;
    }

    private static Guid? ExtractTenantId(string normalizedPath)
    {
        var segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            if (Guid.TryParse(segment, out var tenantId))
            {
                return tenantId;
            }
        }
        return null;
    }
}
