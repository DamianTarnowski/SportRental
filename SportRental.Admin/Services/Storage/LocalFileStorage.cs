using Microsoft.AspNetCore.Hosting;

namespace SportRental.Admin.Services.Storage
{
    public class LocalFileStorage(IWebHostEnvironment env) : IFileStorage
    {
        private readonly IWebHostEnvironment _env = env;

        public async Task<string> SaveAsync(string relativePath, byte[] content, CancellationToken ct = default)
        {
            var fullPath = GetFullPath(relativePath);
            var dir = Path.GetDirectoryName(fullPath)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            await File.WriteAllBytesAsync(fullPath, content, ct);
            return "/" + NormalizeRelativePath(relativePath);
        }

        public async Task<string> SaveAsync(string relativePath, Stream content, CancellationToken ct = default)
        {
            var fullPath = GetFullPath(relativePath);
            var dir = Path.GetDirectoryName(fullPath)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            using var fs = File.Create(fullPath);
            await content.CopyToAsync(fs, ct);
            return "/" + NormalizeRelativePath(relativePath);
        }

        public async Task<byte[]> ReadAsync(string relativePath, CancellationToken ct = default)
        {
            var fullPath = GetFullPath(relativePath);
            return await File.ReadAllBytesAsync(fullPath, ct);
        }

        public Task<bool> ExistsAsync(string relativePath, CancellationToken ct = default)
        {
            var fullPath = GetFullPath(relativePath);
            return Task.FromResult(File.Exists(fullPath));
        }

        public Task<string> SavePrivateAsync(string relativePath, byte[] content, CancellationToken ct = default)
            => SaveAsync(relativePath, content, ct);

        public Task<string> GetPrivateReadUrlAsync(string storageReference, TimeSpan ttl, CancellationToken ct = default)
            => Task.FromResult(storageReference);

        private string GetFullPath(string relativePath)
        {
            var root = Path.GetFullPath(
                _env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"));
            var normalized = NormalizeRelativePath(relativePath);
            var platformPath = normalized.Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.GetFullPath(Path.Combine(root, platformPath));
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

            if (!fullPath.StartsWith(rootPrefix, comparison))
                throw new ArgumentException("Path escapes the storage root", nameof(relativePath));

            return fullPath;
        }

        private static string NormalizeRelativePath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                throw new ArgumentException("Path cannot be empty", nameof(relativePath));

            var normalized = relativePath.Replace('\\', '/').TrimStart('/');
            if (normalized.Split('/').Any(segment =>
                    segment.Length == 0 || segment is "." or ".."))
            {
                throw new ArgumentException("Path contains invalid segments", nameof(relativePath));
            }

            return normalized;
        }
    }
}


