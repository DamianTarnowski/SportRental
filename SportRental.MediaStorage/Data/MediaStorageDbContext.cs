using Microsoft.EntityFrameworkCore;
using SportRental.MediaStorage.Models;

namespace SportRental.MediaStorage.Data;

public class MediaStorageDbContext(DbContextOptions<MediaStorageDbContext> options) : DbContext(options)
{
    public DbSet<StoredFile> Files => Set<StoredFile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var file = modelBuilder.Entity<StoredFile>();
        file.HasKey(x => x.Id);
        file.Property(x => x.OriginalFileName).HasMaxLength(255);
        file.Property(x => x.StoredFileName).HasMaxLength(255);
        file.Property(x => x.ContentType).HasMaxLength(128);
        file.Property(x => x.RelativePath).HasMaxLength(512);
        file.HasIndex(x => new { x.TenantId, x.StoredFileName }).IsUnique();
        file.HasIndex(x => x.CreatedAtUtc);
    }
}
