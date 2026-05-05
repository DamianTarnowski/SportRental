using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using SportRental.Admin.Services.Chat;
using SportRental.Infrastructure.Domain;

namespace SportRental.Admin.Api;

/// <summary>
/// Endpointy dla floating chat voice mode (Azure OpenAI Realtime API + WebRTC).
/// /api/realtime/session — wystawia client-side ephemeral key z server-side master key (z KV).
/// /api/realtime/function/{name} — odbiera function call event z dataChannel WebRTC.
/// </summary>
public static class RealtimeEndpoints
{
    public static void MapRealtimeEndpoints(this IEndpointRouteBuilder app)
    {
        var rt = app.MapGroup("/api/realtime").WithTags("FloatingChatVoice");

        // POST /api/realtime/session — utworzenie sesji realtime + ephemeral key.
        rt.MapPost("/session", [Authorize] async (
            HttpContext httpContext,
            IHttpClientFactory httpFactory,
            IConfiguration config,
            ClaimsPrincipal user,
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            var endpoint = config["OpenAI:RealtimeEndpoint"];
            var apiKey = config["OpenAI:RealtimeApiKey"];
            var deployment = config["OpenAI:RealtimeDeployment"] ?? "gpt-realtime";
            if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(apiKey))
            {
                logger.LogWarning("Realtime endpoint/apikey nieustawione — sekrety w KV?");
                return Results.Problem("Realtime nie skonfigurowany", statusCode: 503);
            }

            var (tenantId, _, email, role) = ResolveUser(user);
            if (tenantId == Guid.Empty) return Results.Forbid();

            var http = httpFactory.CreateClient();
            http.DefaultRequestHeaders.Add("api-key", apiKey);

            // Buduj instructions = ten sam systemowy prompt co dla text chat, ale skondensowany —
            // realtime model dostaje go jako session.instructions zamiast w pierwszej wiadomości.
            var instructions = SystemPromptBuilder.Build(
                userEmail: email,
                userRole: role,
                currentPage: "/admin",
                chatHistory: null);

            var url = $"{endpoint.TrimEnd('/')}/openai/realtimeapi/sessions?api-version=2025-04-01-preview";
            var body = new
            {
                model = deployment,
                voice = "alloy",
                instructions,
                input_audio_format = "pcm16",
                output_audio_format = "pcm16",
                turn_detection = new { type = "server_vad", threshold = 0.5, silence_duration_ms = 500 },
                input_audio_transcription = new { model = "whisper-1" },
                tools = BuildRealtimeTools()
            };

            try
            {
                var resp = await http.PostAsJsonAsync(url, body, ct);
                if (!resp.IsSuccessStatusCode)
                {
                    var err = await resp.Content.ReadAsStringAsync(ct);
                    logger.LogError("Realtime session create failed {Status}: {Body}", resp.StatusCode, err);
                    return Results.Problem("Realtime session error: " + (int)resp.StatusCode, statusCode: 502);
                }
                var json = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
                // Klient dostaje TYLKO ephemeral client_secret + model + endpoint webRTC.
                return Results.Ok(new
                {
                    client_secret = json.GetProperty("client_secret"),
                    model = deployment,
                    webrtc_url = $"{endpoint.TrimEnd('/')}/openai/realtime?api-version=2025-04-01-preview&deployment={deployment}"
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Realtime session exception");
                return Results.Problem(ex.Message, statusCode: 500);
            }
        });

        // POST /api/realtime/function/{name} — wywołanie function call z voice. Body = JSON args.
        // Zwraca {"result": "<json string with tool result>"} — klient przekazuje to z powrotem do dataChannel.
        rt.MapPost("/function/{name}", [Authorize] async (
            string name,
            HttpRequest request,
            ChatToolHandler toolHandler,
            ClaimsPrincipal user,
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            var (tenantId, userId, email, role) = ResolveUser(user);
            if (tenantId == Guid.Empty) return Results.Forbid();

            string argsJson;
            using (var reader = new StreamReader(request.Body))
                argsJson = await reader.ReadToEndAsync(ct);
            if (string.IsNullOrWhiteSpace(argsJson)) argsJson = "{}";

            var ctx = new ChatToolContext
            {
                TenantId = tenantId,
                UserId = userId,
                UserEmail = email,
                UserRole = role,
                CurrentPage = request.Headers["X-Current-Page"].ToString()
            };

            try
            {
                var result = await toolHandler.HandleAsync(name, argsJson, ctx, ct);
                return Results.Ok(new { result });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Realtime function {Name} failed", name);
                return Results.Ok(new { result = JsonSerializer.Serialize(new { error = ex.Message }) });
            }
        });
    }

    /// <summary>Konwertuje ChatTool[] → format wymagany przez Realtime API session config.</summary>
    private static object[] BuildRealtimeTools()
    {
        // Realtime używa nieco innego JSON shape niż chat completions — funkcja jest top-level
        // (bez wrappera "function"), z polami {type:"function", name, description, parameters}.
        return new object[]
        {
            new
            {
                type = "function",
                name = "report_bug",
                description = "Zapisuje zgłoszenie błędu od użytkownika.",
                parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        message = new { type = "string", description = "Opis błędu." },
                        severity = new { type = "string", @enum = new[] { "low", "medium", "high" } }
                    },
                    required = new[] { "message" }
                }
            },
            new
            {
                type = "function",
                name = "submit_feedback",
                description = "Zapisuje ogólny feedback użytkownika.",
                parameters = new
                {
                    type = "object",
                    properties = new
                    {
                        message = new { type = "string", description = "Treść feedbacku." },
                        type = new { type = "string", @enum = new[] { "general", "suggestion", "question", "praise" } }
                    },
                    required = new[] { "message" }
                }
            },
            new
            {
                type = "function",
                name = "get_today_rentals",
                description = "Zwraca wynajmy aktywne na dziś dla mojego tenanta.",
                parameters = new { type = "object", properties = new { }, required = Array.Empty<string>() }
            },
            new
            {
                type = "function",
                name = "get_product_status",
                description = "Sprawdza dostępność produktu po SKU lub nazwie.",
                parameters = new
                {
                    type = "object",
                    properties = new { sku_or_name = new { type = "string" } },
                    required = new[] { "sku_or_name" }
                }
            },
            new
            {
                type = "function",
                name = "get_customer_trust",
                description = "Pobiera trust level klienta po email/telefonie/imieniu.",
                parameters = new
                {
                    type = "object",
                    properties = new { query = new { type = "string" } },
                    required = new[] { "query" }
                }
            },
            new
            {
                type = "function",
                name = "count_active_rentals",
                description = "Zwraca statystyki wynajmów dla mojego tenanta.",
                parameters = new { type = "object", properties = new { }, required = Array.Empty<string>() }
            }
        };
    }

    private static (Guid TenantId, string? UserId, string? Email, string? Role) ResolveUser(ClaimsPrincipal user)
    {
        var tenantClaim = user.FindFirst("tenant-id")?.Value;
        if (!Guid.TryParse(tenantClaim, out var tenantId)) tenantId = Guid.Empty;
        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var email = user.FindFirst(ClaimTypes.Name)?.Value ?? user.FindFirst(ClaimTypes.Email)?.Value;
        var role = user.FindFirst(ClaimTypes.Role)?.Value;
        return (tenantId, userId, email, role);
    }
}
