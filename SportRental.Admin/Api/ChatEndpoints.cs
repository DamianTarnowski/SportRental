using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using SportRental.Admin.Services.Chat;
using SportRental.Infrastructure.Domain;

namespace SportRental.Admin.Api;

public static class ChatEndpoints
{
    public static void MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        var chat = app.MapGroup("/api/chat").WithTags("FloatingChat");

        // POST /api/chat/send — wysyła wiadomość do asystenta z aktualnym kontekstem strony,
        // dostaje odpowiedź (po ewentualnych tool calls). Tylko zalogowani.
        chat.MapPost("/send", [Authorize] async (
            ChatSendRequest req,
            ClaimsPrincipal user,
            FloatingChatService session,
            OpenAiChatService openAi,
            ChatToolHandler toolHandler,
            ILogger<OpenAiChatService> logger,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Message))
                return Results.BadRequest(new { error = "Message required" });

            var (tenantId, userId, email, role) = ResolveUser(user);
            if (tenantId == Guid.Empty)
                return Results.Forbid();

            // Dolewamy user message do session history (in-memory per scope) — Blazor Server scoped
            // kontekst ma TĘ SAMĄ instancję FloatingChatService dla całej sesji circuit.
            session.AddUserMessage(req.Message);

            var systemPrompt = SystemPromptBuilder.Build(
                userEmail: email,
                userRole: role,
                currentPage: req.CurrentPage,
                chatHistory: session.BuildHistoryForPrompt(10));

            // Ostatnie max 8 wymian z UI history (poza ostatnia wiadomością user która jest w prompt)
            var turns = session.Messages
                .Take(session.Messages.Count - 1)  // bez ostatniego usera (system_prompt go ma)
                .TakeLast(8)
                .Select(m => new ChatTurn(m.Content, m.IsUser))
                .Append(new ChatTurn(req.Message, true))
                .ToList();

            var context = new ChatToolContext
            {
                TenantId = tenantId,
                UserId = userId,
                UserEmail = email,
                UserRole = role,
                CurrentPage = req.CurrentPage,
                ContextJson = req.ContextJson
            };

            var result = await openAi.ChatAsync(
                systemPrompt: systemPrompt,
                history: turns,
                tools: ChatToolDefinitions.All,
                toolHandler: (name, args) => toolHandler.HandleAsync(name, args, context, ct),
                ct: ct);

            session.AddAssistantMessage(result.Content);

            return Results.Ok(new ChatSendResponse
            {
                Content = result.Content,
                ToolsExecuted = result.ToolCallsExecuted.Select(t => t.FunctionName).ToList(),
                Error = result.Error
            });
        });

        // POST /api/chat/clear — wyczyść historię w bieżącej sesji.
        chat.MapPost("/clear", [Authorize] (FloatingChatService session) =>
        {
            session.Clear();
            return Results.NoContent();
        });

        // GET /api/chat/settings — pobiera globalne settings (każdy zalogowany; UI floating
        // chat-a z tego korzysta przy starcie żeby wiedzieć czy pokazać toggle modelu).
        chat.MapGet("/settings", [Authorize] async (
            ChatSettingsService settings,
            CancellationToken ct) =>
        {
            var s = await settings.GetAsync(ct);
            return Results.Ok(new
            {
                defaultModel = s.DefaultModel,
                allowUserModelChoice = s.AllowUserModelChoice,
                allowedModels = OpenAiChatService.AllowedDeployments.ToArray()
            });
        });

        // PUT /api/chat/settings — TYLKO SuperAdmin może zmieniać.
        chat.MapPut("/settings", [Authorize(Roles = "SuperAdmin")] async (
            ChatSettingsRequest req,
            ClaimsPrincipal user,
            ChatSettingsService settings,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.DefaultModel))
                return Results.BadRequest(new { error = "DefaultModel required" });
            try
            {
                var who = user.FindFirst(ClaimTypes.Name)?.Value ?? user.FindFirst(ClaimTypes.Email)?.Value;
                await settings.UpdateAsync(req.DefaultModel, req.AllowUserModelChoice, who, ct);
                return Results.NoContent();
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // POST /api/feedback — bezpośredni zapis feedbacku bez asystenta (np. przycisk
        // „Zgłoś błąd" z poziomu UI, albo 👎 pod odpowiedzią asystenta).
        chat.MapPost("/feedback", [Authorize] async (
            FeedbackDirectRequest req,
            ClaimsPrincipal user,
            FeedbackService feedback,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Message))
                return Results.BadRequest(new { error = "Message required" });

            var (tenantId, userId, email, role) = ResolveUser(user);
            if (tenantId == Guid.Empty)
                return Results.Forbid();

            var entry = new UserFeedback
            {
                TenantId = tenantId,
                UserId = userId,
                UserEmail = email,
                UserRole = role,
                CurrentPage = req.CurrentPage,
                Type = ParseType(req.Type),
                Message = req.Message,
                ContextJson = req.ContextJson
            };
            var id = await feedback.SaveAsync(entry, ct);
            return Results.Ok(new { id });
        });
    }

    private static FeedbackType ParseType(string? raw) => raw?.ToLowerInvariant() switch
    {
        "bug" => FeedbackType.Bug,
        "suggestion" => FeedbackType.Suggestion,
        "question" => FeedbackType.Question,
        "praise" => FeedbackType.Praise,
        _ => FeedbackType.General
    };

    private static (Guid TenantId, string? UserId, string? Email, string? Role) ResolveUser(ClaimsPrincipal user)
    {
        var tenantClaim = user.FindFirst("tenant-id")?.Value;
        if (!Guid.TryParse(tenantClaim, out var tenantId))
            tenantId = Guid.Empty;
        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var email = user.FindFirst(ClaimTypes.Name)?.Value ?? user.FindFirst(ClaimTypes.Email)?.Value;
        var role = user.FindFirst(ClaimTypes.Role)?.Value;
        return (tenantId, userId, email, role);
    }
}

public sealed class ChatSendRequest
{
    public string Message { get; set; } = string.Empty;
    public string? CurrentPage { get; set; }
    public string? ContextJson { get; set; }
}

public sealed class ChatSendResponse
{
    public string Content { get; set; } = string.Empty;
    public List<string> ToolsExecuted { get; set; } = new();
    public string? Error { get; set; }
}

public sealed class FeedbackDirectRequest
{
    public string Message { get; set; } = string.Empty;
    public string? Type { get; set; }
    public string? CurrentPage { get; set; }
    public string? ContextJson { get; set; }
}

public sealed class ChatSettingsRequest
{
    public string DefaultModel { get; set; } = "gpt-5.5";
    public bool AllowUserModelChoice { get; set; } = true;
}
