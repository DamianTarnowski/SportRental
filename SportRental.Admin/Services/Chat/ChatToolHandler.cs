using System.Text.Json;
using SportRental.Infrastructure.Domain;

namespace SportRental.Admin.Services.Chat;

/// <summary>
/// Dispatcher dla function tool calls od asystenta. Bierze (functionName, argsJson) i
/// wykonuje akcję — w fazie 1 to zapisy do tabeli sr_user_feedbacks. Wynik zwraca jako
/// JSON (krótkie potwierdzenie z id), żeby model mógł poinformować usera.
/// </summary>
public sealed class ChatToolHandler
{
    private readonly FeedbackService _feedback;
    private readonly ReadToolService _read;
    private readonly ILogger<ChatToolHandler> _logger;

    public ChatToolHandler(FeedbackService feedback, ReadToolService read, ILogger<ChatToolHandler> logger)
    {
        _feedback = feedback;
        _read = read;
        _logger = logger;
    }

    /// <summary>
    /// Przetwarza pojedynczy tool call w kontekście aktualnego użytkownika.
    /// </summary>
    public async Task<string> HandleAsync(
        string functionName,
        string argsJson,
        ChatToolContext context,
        CancellationToken ct = default)
    {
        try
        {
            return functionName switch
            {
                // Faza 1 — write tools
                "report_bug" => await HandleReportBug(argsJson, context, ct),
                "submit_feedback" => await HandleSubmitFeedback(argsJson, context, ct),
                // Faza 2 — read tools
                "get_today_rentals" => await _read.GetTodayRentalsAsync(context.TenantId, ct),
                "get_product_status" => await HandleGetProduct(argsJson, context, ct),
                "get_customer_trust" => await HandleGetCustomer(argsJson, context, ct),
                "count_active_rentals" => await _read.CountActiveRentalsAsync(context.TenantId, ct),
                _ => JsonSerializer.Serialize(new { error = $"Unknown tool: {functionName}" })
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tool {Name} failed", functionName);
            return JsonSerializer.Serialize(new { error = "tool_execution_failed", details = ex.Message });
        }
    }

    private async Task<string> HandleGetProduct(string argsJson, ChatToolContext ctx, CancellationToken ct)
    {
        var args = JsonSerializer.Deserialize<GetProductArgs>(argsJson) ?? new GetProductArgs();
        return await _read.GetProductStatusAsync(ctx.TenantId, args.SkuOrName ?? string.Empty, ct);
    }

    private async Task<string> HandleGetCustomer(string argsJson, ChatToolContext ctx, CancellationToken ct)
    {
        var args = JsonSerializer.Deserialize<GetCustomerArgs>(argsJson) ?? new GetCustomerArgs();
        return await _read.GetCustomerTrustAsync(ctx.TenantId, args.Query ?? string.Empty, ct);
    }

    private sealed class GetProductArgs
    {
        [System.Text.Json.Serialization.JsonPropertyName("sku_or_name")] public string? SkuOrName { get; set; }
    }

    private sealed class GetCustomerArgs
    {
        [System.Text.Json.Serialization.JsonPropertyName("query")] public string? Query { get; set; }
    }

    private async Task<string> HandleReportBug(string argsJson, ChatToolContext ctx, CancellationToken ct)
    {
        var args = JsonSerializer.Deserialize<ReportBugArgs>(argsJson)
                   ?? new ReportBugArgs { Message = "(brak treści)" };

        var feedback = new UserFeedback
        {
            TenantId = ctx.TenantId,
            UserId = ctx.UserId,
            UserEmail = ctx.UserEmail,
            UserRole = ctx.UserRole,
            CurrentPage = ctx.CurrentPage,
            Type = FeedbackType.Bug,
            Message = $"[severity={args.Severity ?? "medium"}] {args.Message}",
            ContextJson = ctx.ContextJson
        };
        var id = await _feedback.SaveAsync(feedback, ct);
        return JsonSerializer.Serialize(new { saved = true, id, severity = args.Severity ?? "medium" });
    }

    private async Task<string> HandleSubmitFeedback(string argsJson, ChatToolContext ctx, CancellationToken ct)
    {
        var args = JsonSerializer.Deserialize<SubmitFeedbackArgs>(argsJson)
                   ?? new SubmitFeedbackArgs { Message = "(brak treści)" };

        var type = args.Type?.ToLowerInvariant() switch
        {
            "suggestion" => FeedbackType.Suggestion,
            "question" => FeedbackType.Question,
            "praise" => FeedbackType.Praise,
            _ => FeedbackType.General
        };

        var feedback = new UserFeedback
        {
            TenantId = ctx.TenantId,
            UserId = ctx.UserId,
            UserEmail = ctx.UserEmail,
            UserRole = ctx.UserRole,
            CurrentPage = ctx.CurrentPage,
            Type = type,
            Message = args.Message,
            ContextJson = ctx.ContextJson
        };
        var id = await _feedback.SaveAsync(feedback, ct);
        return JsonSerializer.Serialize(new { saved = true, id, type = type.ToString() });
    }

    private sealed class ReportBugArgs
    {
        [System.Text.Json.Serialization.JsonPropertyName("message")] public string Message { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("severity")] public string? Severity { get; set; }
    }

    private sealed class SubmitFeedbackArgs
    {
        [System.Text.Json.Serialization.JsonPropertyName("message")] public string Message { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonPropertyName("type")] public string? Type { get; set; }
    }
}

/// <summary>Kontekst aktualnego użytkownika przekazywany do tool handlera.</summary>
public sealed class ChatToolContext
{
    public required Guid TenantId { get; init; }
    public string? UserId { get; init; }
    public string? UserEmail { get; init; }
    public string? UserRole { get; init; }
    public string? CurrentPage { get; init; }
    /// <summary>Dodatkowy JSON z kontekstem (historia ostatnich N message itp.).</summary>
    public string? ContextJson { get; init; }
}
