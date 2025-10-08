using Microsoft.AspNetCore.Hosting;

namespace SportRental.Admin.Services.Storage
{
    public class LocalFileStorage(IWebHostEnvironment env) : IFileStorage
    {
        private readonly IWebHostEnvironment _env = env;

        public async Task<string> SaveAsync(string relativePath, byte[] content, CancellationToken ct = default)
        {
            var root = Path.Combine(_env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"));
            var fullPath = Path.Combine(root, relativePath.Replace("/", Path.DirectorySeparatorChar.ToString()));
            var dir = Path.GetDirectoryName(fullPath)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            await File.WriteAllBytesAsync(fullPath, content, ct);
            return "/" + relativePath.Replace("\\", "/");
        }

        public async Task<string> SaveAsync(string relativePath, Stream content, CancellationToken ct = default)
        {
            var root = Path.Combine(_env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"));
            var fullPath = Path.Combine(root, relativePath.Replace("/", Path.DirectorySeparatorChar.ToString()));
            var dir = Path.GetDirectoryName(fullPath)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            using var fs = File.Create(fullPath);
            await content.CopyToAsync(fs, ct);
            return "/" + relativePath.Replace("\\", "/");
        }

        public async Task<byte[]> ReadAsync(string relativePath, CancellationToken ct = default)
        {
            var root = Path.Combine(_env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"));
            var fullPath = Path.Combine(root, relativePath.Replace("/", Path.DirectorySeparatorChar.ToString()));
            return await File.ReadAllBytesAsync(fullPath, ct);
        }

        public Task<bool> ExistsAsync(string relativePath, CancellationToken ct = default)
        {
            var root = Path.Combine(_env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"));
            var fullPath = Path.Combine(root, relativePath.Replace("/", Path.DirectorySeparatorChar.ToString()));
            return Task.FromResult(File.Exists(fullPath));
        }
    }
}


