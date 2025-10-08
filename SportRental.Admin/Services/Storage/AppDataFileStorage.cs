using Microsoft.Extensions.Configuration;

namespace SportRental.Admin.Services.Storage
{
    // Prosty, wewnętrzny blob na dysku poza wwwroot, z własną przestrzenią URL (domyślnie /files)
    public sealed class AppDataFileStorage : IFileStorage
    {
        private readonly string _rootPath;
        private readonly string _requestBase;

        public AppDataFileStorage(IConfiguration configuration)
        {
            _rootPath = configuration["Storage:RootPath"]
                        ?? Path.Combine(AppContext.BaseDirectory, "App_Data");
            Directory.CreateDirectory(_rootPath);
            _requestBase = configuration["Storage:RequestPath"] ?? "/files"; // musi być zmapowane w Program.cs
        }

        public async Task<string> SaveAsync(string relativePath, byte[] content, CancellationToken ct = default)
        {
            using var ms = new MemoryStream(content);
            return await SaveAsync(relativePath, ms, ct);
        }

        public async Task<string> SaveAsync(string relativePath, Stream content, CancellationToken ct = default)
        {
            var fullPath = GetFullPath(relativePath);
            var dir = Path.GetDirectoryName(fullPath)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            await using var fs = File.Create(fullPath);
            await content.CopyToAsync(fs, ct);
            return BuildPublicUrl(relativePath);
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

        private string GetFullPath(string relativePath)
        {
            var safe = relativePath.Replace('\\', '/').TrimStart('/');
            return Path.Combine(_rootPath, safe.Replace('/', Path.DirectorySeparatorChar));
        }

        private string BuildPublicUrl(string relativePath)
        {
            var safe = relativePath.Replace('\\', '/').TrimStart('/');
            return _requestBase.TrimEnd('/') + "/" + safe;
        }
    }
}


