using Microsoft.EntityFrameworkCore;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;

namespace SportRental.Admin.Services.Chat;

/// <summary>
/// Persystuje konwersacje i wiadomości żeby asystent miał long-term memory między sesjami.
/// Per (tenant, user) — SuperAdmin widzi tylko swoje. RODO-friendly: trzymamy treść,
/// ale można łatwo dodać retention job (np. usuwaj wiadomości starsze niż 90 dni).
/// </summary>
public sealed class ChatPersistenceService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
    private readonly ILogger<ChatPersistenceService> _logger;

    public ChatPersistenceService(IDbContextFactory<ApplicationDbContext> dbFactory, ILogger<ChatPersistenceService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    /// <summary>
    /// Zwraca conversationId — tworzy nową gdy starsza niż 30 min od ostatniej wymiany.
    /// </summary>
    public async Task<Guid> GetOrCreateConversationAsync(
        Guid tenantId, string? userId, string? userEmail, string? userRole, string source, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty || string.IsNullOrWhiteSpace(userId))
            return Guid.Empty;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var thirtyMinutesAgo = DateTime.UtcNow.AddMinutes(-30);

        // Najnowsza konwersacja tego usera bez ended_at lub świeża.
        var recent = await db.ChatConversations
            .Where(c => c.TenantId == tenantId && c.UserId == userId && c.StartedAtUtc > thirtyMinutesAgo)
            .OrderByDescending(c => c.StartedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (recent != null) return recent.Id;

        var conv = new ChatConversation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            UserEmail = userEmail,
            UserRole = userRole,
            Source = source,
            StartedAtUtc = DateTime.UtcNow
        };
        db.ChatConversations.Add(conv);
        await db.SaveChangesAsync(ct);
        return conv.Id;
    }

    public async Task SaveMessageAsync(
        Guid conversationId, Guid tenantId, string role, string content,
        string? currentPage, string? toolCallsJson, CancellationToken ct = default)
    {
        if (conversationId == Guid.Empty || string.IsNullOrWhiteSpace(content)) return;

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            db.ChatMessages.Add(new ChatMessage
            {
                Id = Guid.NewGuid(),
                ConversationId = conversationId,
                TenantId = tenantId,
                Role = role,
                Content = content.Length > 16000 ? content[..16000] + "…[trim]" : content,
                CurrentPage = currentPage,
                ToolCallsJson = toolCallsJson,
                CreatedAtUtc = DateTime.UtcNow
            });

            // Zwiększ licznik na rozmowie (best-effort, bez transakcji).
            var conv = await db.ChatConversations.FirstOrDefaultAsync(c => c.Id == conversationId, ct);
            if (conv != null) { conv.MessageCount++; }

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Persystencja jest best-effort — nie blokuj rozmowy gdy DB padnie.
            _logger.LogWarning(ex, "Failed to persist chat message");
        }
    }

    /// <summary>
    /// Pobiera ostatnie max N wymian z poprzednich sesji tego usera dla cross-session memory.
    /// Wynik formatowany pod system prompt.
    /// </summary>
    public async Task<string> BuildCrossSessionHistoryAsync(
        Guid tenantId, string userId, int maxMessages = 10, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty || string.IsNullOrWhiteSpace(userId))
            return string.Empty;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var sevenDaysAgo = DateTime.UtcNow.AddDays(-7);

        var messages = await db.ChatMessages
            .Where(m => m.TenantId == tenantId
                     && m.Conversation != null && m.Conversation.UserId == userId
                     && m.CreatedAtUtc >= sevenDaysAgo)
            .OrderByDescending(m => m.CreatedAtUtc)
            .Take(maxMessages * 2)  // weź więcej, podzielimy na rundy
            .Select(m => new { m.Role, m.Content, m.CreatedAtUtc })
            .ToListAsync(ct);

        if (messages.Count == 0) return string.Empty;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine();
        sb.AppendLine("## TWOJA HISTORIA Z TYM UŻYTKOWNIKIEM (ostatnie 7 dni)");
        sb.AppendLine("Te wiadomości pochodzą z poprzednich sesji — używaj jako kontekst, nie powtarzaj.");
        // Reverse — od najstarszych do najnowszych
        foreach (var m in messages.AsEnumerable().Reverse())
        {
            var role = m.Role == "user" ? "Użytkownik" : "Asystent";
            var content = m.Content.Length > 250 ? m.Content[..250] + "…" : m.Content;
            sb.AppendLine($"- [{m.CreatedAtUtc:dd.MM HH:mm}] {role}: {content}");
        }
        return sb.ToString();
    }
}
