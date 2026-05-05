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
    private readonly string _deployment;
    private readonly ILogger<OpenAiChatService> _logger;

    public OpenAiChatService(AzureOpenAIClient client, IConfiguration config, ILogger<OpenAiChatService> logger)
    {
        _client = client;
        _deployment = config["OpenAI:TextDeployment"] ?? "gpt-5.5";
        _logger = logger;
    }

    public async Task<ChatCompletionResult> ChatAsync(
        string systemPrompt,
        IReadOnlyList<ChatTurn> history,
        IReadOnlyList<ChatTool> tools,
        Func<string, string, Task<string>> toolHandler,
        CancellationToken ct = default)
    {
        var chat = _client.GetChatClient(_deployment);

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

        var options = new ChatCompletionOptions();
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
