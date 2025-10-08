namespace SportRental.Admin.Services.Storage
{
    public interface IFileStorage
    {
        Task<string> SaveAsync(string relativePath, byte[] content, CancellationToken ct = default);
        Task<string> SaveAsync(string relativePath, Stream content, CancellationToken ct = default);
        Task<byte[]> ReadAsync(string relativePath, CancellationToken ct = default);
        Task<bool> ExistsAsync(string relativePath, CancellationToken ct = default);
    }
}


