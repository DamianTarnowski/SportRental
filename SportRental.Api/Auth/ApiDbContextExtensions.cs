using Microsoft.EntityFrameworkCore;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;

namespace SportRental.Api.Auth;

/// <summary>
/// Extension methods to configure API-specific entities (like RefreshToken) in ApplicationDbContext
/// </summary>
public static class ApiDbContextExtensions
{
    public static void ConfigureApiEntities(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("RefreshTokens");
            entity.HasKey(rt => rt.Id);
            entity.HasIndex(rt => rt.Token).IsUnique();
            entity.HasIndex(rt => rt.UserId);
            entity.HasIndex(rt => rt.ExpiresAtUtc);
            entity.Property(rt => rt.Token).HasMaxLength(128).IsRequired();
            entity.Property(rt => rt.RevokedReason).HasMaxLength(200);
            entity.Property(rt => rt.ReplacedByToken).HasMaxLength(128);
        });
    }
    
    /// <summary>
    /// Ensures the database is created and RefreshToken entity is registered
    /// Call this during application startup
    /// </summary>
    public static async Task EnsureApiDatabaseAsync(this ApplicationDbContext dbContext)
    {
        // This will create the RefreshTokens table if it doesn't exist
        await dbContext.Database.EnsureCreatedAsync();
    }
}
