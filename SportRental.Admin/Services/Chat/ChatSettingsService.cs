using Microsoft.EntityFrameworkCore;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;

namespace SportRental.Admin.Services.Chat;

/// <summary>
/// Get/Update globalnych ustawień chat (domyślny model + czy user może wybierać).
/// Singleton: jeden rekord z Id = Guid.Empty. Tworzony lazy gdy brak.
/// </summary>
public sealed class ChatSettingsService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
    private readonly ILogger<ChatSettingsService> _logger;

    public ChatSettingsService(IDbContextFactory<ApplicationDbContext> dbFactory, ILogger<ChatSettingsService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task<ChatSettings> GetAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var s = await db.ChatSettings.FirstOrDefaultAsync(x => x.Id == Guid.Empty, ct);
        if (s != null) return s;

        // Lazy create with defaults gdy brak.
        s = new ChatSettings
        {
            Id = Guid.Empty,
            DefaultModel = "gpt-5.5",
            AllowUserModelChoice = true,
            UpdatedAtUtc = DateTime.UtcNow
        };
        db.ChatSettings.Add(s);
        try { await db.SaveChangesAsync(ct); } catch { /* race-conditions tolerujemy */ }
        return s;
    }

    public async Task UpdateAsync(string defaultModel, bool allowUserChoice, string? updatedBy, CancellationToken ct = default)
    {
        if (!OpenAiChatService.AllowedDeployments.Contains(defaultModel))
            throw new ArgumentException($"Niedozwolony model: {defaultModel}");

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var s = await db.ChatSettings.FirstOrDefaultAsync(x => x.Id == Guid.Empty, ct);
        if (s == null)
        {
            s = new ChatSettings { Id = Guid.Empty };
            db.ChatSettings.Add(s);
        }
        s.DefaultModel = defaultModel;
        s.AllowUserModelChoice = allowUserChoice;
        s.UpdatedAtUtc = DateTime.UtcNow;
        s.UpdatedBy = updatedBy;
        await db.SaveChangesAsync(ct);
        _logger.LogInformation("ChatSettings updated by {User}: model={Model} allowChoice={Allow}", updatedBy, defaultModel, allowUserChoice);
    }
}
