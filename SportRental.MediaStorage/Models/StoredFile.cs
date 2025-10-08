namespace SportRental.MediaStorage.Models;

public class StoredFile
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public required string OriginalFileName { get; set; }
    public required string StoredFileName { get; set; }
    public required string ContentType { get; set; }
    public long Size { get; set; }
    public required string RelativePath { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string? Sha256 { get; set; }
}
