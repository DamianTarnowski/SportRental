namespace SportRental.Admin.Services.Storage
{
    public interface IFileStorage
    {
        Task<string> SaveAsync(string relativePath, byte[] content, CancellationToken ct = default);
        Task<string> SaveAsync(string relativePath, Stream content, CancellationToken ct = default);
        Task<byte[]> ReadAsync(string relativePath, CancellationToken ct = default);
        Task<bool> ExistsAsync(string relativePath, CancellationToken ct = default);

        /// <summary>
        /// Saves a file to private storage (no anonymous access). Returns an opaque storage reference
        /// (e.g. blob relative path) to persist. Use <see cref="GetPrivateReadUrlAsync"/> to obtain a
        /// short-lived URL when serving the file to a user.
        /// </summary>
        Task<string> SavePrivateAsync(string relativePath, byte[] content, CancellationToken ct = default);

        /// <summary>
        /// Resolves a stored private-storage reference to a time-limited, read-only URL.
        /// </summary>
        Task<string> GetPrivateReadUrlAsync(string storageReference, TimeSpan ttl, CancellationToken ct = default);
    }
}
