namespace SportRental.MediaStorage.Options;

public class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>
    /// Root path on disk where files will be persisted. Should be mounted volume in container.
    /// </summary>
    public string RootPath { get; set; } = Path.Combine(AppContext.BaseDirectory, "media");

    /// <summary>
    /// Base URL exposed by reverse proxy for file downloads (e.g., https://media.example.com).
    /// Optional; when null relative URLs will be returned.
    /// </summary>
    public string? PublicBaseUrl { get; set; }

    /// <summary>
    /// Comma separated list of allowed file extensions (without dot).
    /// </summary>
    public string[] AllowedExtensions { get; set; } = ["jpg", "jpeg", "png", "webp", "pdf"];

    /// <summary>
    /// Maximum allowed file size in bytes.
    /// </summary>
    public long MaxFileSizeBytes { get; set; } = 5 * 1024 * 1024;
}
