using System.Text.Json;
using Azure.AI.OpenAI;
using OpenAI.Chat;

namespace SportRental.Admin.Services.Chat;

/// <summary>
/// Wrapper na Azure OpenAI ChatClient — robi multi-turn z function calling.
/// Pętla: send messages → jeśli tool_calls, wykonaj przez handler i dolej outputs do
/// messages, ponowny request. Max 3 rundy żeby model nie pętlił w nieskończoność.
/// </summary>
public sealed class OpenAiChatService
{
    private readonly AzureOpenAIClient _client;
    private readonly string _defaultDeployment;
    private readonly ILogger<OpenAiChatService> _logger;

    /// <summary>Whitelist deploymentów na które user może się przełączyć z UI.</summary>
    public static readonly HashSet<string> AllowedDeployments = new(StringComparer.OrdinalIgnoreCase)
    {
        "gpt-5.5",
        "gpt-5.4-mini"
    };

    public OpenAiChatService(AzureOpenAIClient client, IConfiguration config, ILogger<OpenAiChatService> logger)
    {
        _client = client;
        _defaultDeployment = config["OpenAI:TextDeployment"] ?? "gpt-5.5";
        _logger = logger;
    }

    public string DefaultDeployment => _defaultDeployment;

    public async Task<ChatCompletionResult> ChatAsync(
        string systemPrompt,
        IReadOnlyList<ChatTurn> history,
        IReadOnlyList<ChatTool> tools,
        Func<string, string, Task<string>> toolHandler,
        string? overrideDeployment = null,
        CancellationToken ct = default)
    {
        // Hard cap całego flow chat (3 rundy + tool dispatch). Po 60s dla całego ChatAsync
        // zwracamy błąd zamiast utrzymywać blokujące _isThinking w UI w nieskończoność.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(60));
        ct = timeoutCts.Token;

        // Sprawdź whitelist override → fallback na default jeśli ktoś podał śmieci
        var deployment = !string.IsNullOrWhiteSpace(overrideDeployment) && AllowedDeployments.Contains(overrideDeployment)
            ? overrideDeployment
            : _defaultDeployment;
        var chat = _client.GetChatClient(deployment);

        // Buildujemy listę wiadomości — DeveloperChatMessage (nowy odpowiednik System dla
        // gpt-5+) plus historia user/assistant. ToolChatMessage jest dodawany w pętli niżej.
        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(systemPrompt)
        };
        foreach (var turn in history)
        {
            messages.Add(turn.IsUser ? new UserChatMessage(turn.Content) : new AssistantChatMessage(turn.Content));
        }

        var options = new ChatCompletionOptions
        {
            // Niski reasoning effort = szybsze odpowiedzi. Dla chat-asystenta UI nie potrzebuje
            // głębokiego "thinking" (jak w długich kodach/analizach) — chcemy interaktywności.
            // Action: zauważalnie skraca czas pierwszego tokenu z ~5-8s do ~1-2s na gpt-5.x.
            ReasoningEffortLevel = ChatReasoningEffortLevel.Low
        };
        foreach (var t in tools) options.Tools.Add(t);

        var toolCallsExecuted = new List<ExecutedToolCall>();

        for (int round = 0; round < 3; round++)
        {
            ChatCompletion completion;
            try
            {
                var resp = await chat.CompleteChatAsync(messages, options, ct);
                completion = resp.Value;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Azure OpenAI ChatCompletion failed (round {Round})", round);
                return new ChatCompletionResult
                {
                    Content = "Przepraszam, asystent jest chwilowo niedostępny. Spróbuj za moment.",
                    ToolCallsExecuted = toolCallsExecuted,
                    Error = ex.Message
                };
            }

            if (completion.FinishReason == ChatFinishReason.Stop)
            {
                var text = string.Concat(completion.Content.Select(c => c.Text));
                return new ChatCompletionResult { Content = text, ToolCallsExecuted = toolCallsExecuted };
            }

            if (completion.FinishReason == ChatFinishReason.ToolCalls)
            {
                // Dodajemy assistant message z tool_calls (bez treści) do historii.
                messages.Add(new AssistantChatMessage(completion));

                foreach (var tc in completion.ToolCalls)
                {
                    var argsJson = tc.FunctionArguments?.ToString() ?? "{}";
                    string toolResult;
                    try
                    {
                        toolResult = await toolHandler(tc.FunctionName, argsJson);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Tool call {Name} failed", tc.FunctionName);
                        toolResult = JsonSerializer.Serialize(new { error = ex.Message });
                    }

                    toolCallsExecuted.Add(new ExecutedToolCall(tc.FunctionName, argsJson, toolResult));
                    messages.Add(new ToolChatMessage(tc.Id, toolResult));
                }
                continue; // kolejna runda
            }

            // Inne finish_reason (length / content_filter) — zwracamy co jest.
            var partial = completion.Content.Count > 0 ? completion.Content[0].Text : string.Empty;
            return new ChatCompletionResult
            {
                Content = string.IsNullOrWhiteSpace(partial)
                    ? "Odpowiedź została obcięta lub odfiltrowana."
                    : partial,
                ToolCallsExecuted = toolCallsExecuted,
                Error = $"finish_reason={completion.FinishReason}"
            };
        }

        return new ChatCompletionResult
        {
            Content = "Asystent nie mógł zakończyć rozważań w 3 rundach — spróbuj zadać pytanie inaczej.",
            ToolCallsExecuted = toolCallsExecuted,
            Error = "max_rounds_exceeded"
        };
    }
}

public sealed record ChatTurn(string Content, bool IsUser);

public sealed record ExecutedToolCall(string FunctionName, string ArgumentsJson, string ResultJson);

public sealed class ChatCompletionResult
{
    public string Content { get; set; } = string.Empty;
    public IReadOnlyList<ExecutedToolCall> ToolCallsExecuted { get; set; } = Array.Empty<ExecutedToolCall>();
    public string? Error { get; set; }
}
