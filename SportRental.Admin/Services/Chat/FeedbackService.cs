using Microsoft.EntityFrameworkCore;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;

namespace SportRental.Admin.Services.Chat;

/// <summary>
/// Zapis i odczyt zgłoszeń użytkowników. Tworzony przez floating chat tool calls
/// (report_bug, submit_feedback) albo bezpośrednio przez UI „Zgłoś błąd".
/// </summary>
public sealed class FeedbackService
{
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;
    private readonly ILogger<FeedbackService> _logger;

    public FeedbackService(IDbContextFactory<ApplicationDbContext> dbFactory, ILogger<FeedbackService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task<Guid> SaveAsync(UserFeedback feedback, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        // Nie ustawiamy SetTenant tutaj — feedback ZAWSZE zapisywany z konkretnym TenantId,
        // ale zapis powinien działać niezależnie od stanu filtrów (insert nie idzie przez query filter).
        if (feedback.Id == Guid.Empty) feedback.Id = Guid.NewGuid();
        if (feedback.CreatedAtUtc == default) feedback.CreatedAtUtc = DateTime.UtcNow;
        db.UserFeedbacks.Add(feedback);
        await db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "UserFeedback saved Id={Id} Tenant={Tenant} Type={Type} Page={Page}",
            feedback.Id, feedback.TenantId, feedback.Type, feedback.CurrentPage);
        return feedback.Id;
    }

    /// <summary>
    /// Lista zgłoszeń. Jeśli `tenantId == null` — cross-tenant view dla SuperAdmin.
    /// Inaczej tylko dla podanego tenanta (Owner widzi swoje).
    /// </summary>
    public async Task<List<UserFeedback>> ListAsync(
        Guid? tenantId,
        FeedbackType? type = null,
        bool? isResolved = null,
        int limit = 200,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        // Wyłączamy query filter i sami filtrujemy explicite — pozwala SuperAdminowi widzieć wszystko
        // i jednocześnie kontroluje że Owner widzi tylko swój tenant.
        var query = db.UserFeedbacks.IgnoreQueryFilters().AsNoTracking();

        if (tenantId.HasValue)
            query = query.Where(uf => uf.TenantId == tenantId.Value);

        if (type.HasValue)
            query = query.Where(uf => uf.Type == type.Value);

        if (isResolved.HasValue)
            query = query.Where(uf => uf.IsResolved == isResolved.Value);

        return await query
            .OrderByDescending(uf => uf.CreatedAtUtc)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task ResolveAsync(Guid feedbackId, string resolvedBy, string? notes, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var fb = await db.UserFeedbacks.IgnoreQueryFilters()
            .FirstOrDefaultAsync(uf => uf.Id == feedbackId, ct);
        if (fb is null) return;

        fb.IsResolved = true;
        fb.ResolvedAtUtc = DateTime.UtcNow;
        fb.ResolvedBy = resolvedBy;
        fb.ResolutionNotes = notes;
        await db.SaveChangesAsync(ct);
    }
}
