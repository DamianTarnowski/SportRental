namespace SportRental.Admin.Services.Chat;

/// <summary>
/// Scoped state pomocniczego floating chat — historia wiadomości pomiędzy nawigacjami
/// w obrębie pojedynczej sesji Blazor Server. Nic nie persystuje na dysk; po reload
/// strony rozmowa znika (świadomy choice — to chat „pomocowy", nie historia).
/// </summary>
public sealed class FloatingChatService
{
    private readonly List<FloatingChatMessage> _messages = new();
    private int _nextIndex;

    public IReadOnlyList<FloatingChatMessage> Messages => _messages;

    public bool IsOpen { get; set; }

    public FloatingChatMessage AddUserMessage(string content)
    {
        var msg = new FloatingChatMessage
        {
            Content = content,
            IsUser = true,
            Timestamp = DateTimeOffset.UtcNow,
            Index = _nextIndex++
        };
        _messages.Add(msg);
        return msg;
    }

    public FloatingChatMessage AddAssistantMessage(string content)
    {
        var msg = new FloatingChatMessage
        {
            Content = content,
            IsUser = false,
            Timestamp = DateTimeOffset.UtcNow,
            Index = _nextIndex++
        };
        _messages.Add(msg);
        return msg;
    }

    public void Clear()
    {
        _messages.Clear();
        _nextIndex = 0;
    }

    /// <summary>
    /// Buduje kompaktową reprezentację ostatnich N wymian dla system prompt — żeby model
    /// pamiętał czego uzytkownik chce w obrębie tej rozmowy. Skracamy długie wiadomości.
    /// </summary>
    public string BuildHistoryForPrompt(int lastN = 10)
    {
        if (_messages.Count == 0) return string.Empty;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine();
        sb.AppendLine("=== HISTORIA TEJ ROZMOWY ===");
        foreach (var m in _messages.TakeLast(lastN))
        {
            var role = m.IsUser ? "Użytkownik" : "Asystent";
            var content = m.Content.Length > 400 ? m.Content[..400] + "..." : m.Content;
            sb.AppendLine($"{role}: {content}");
        }
        return sb.ToString();
    }
}

public sealed class FloatingChatMessage
{
    public string Content { get; set; } = string.Empty;
    public bool IsUser { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public int Index { get; init; }
    /// <summary>"positive" / "negative" / null. Ustawia user przyciskiem 👍/👎 pod wiadomością asystenta.</summary>
    public string? Feedback { get; set; }
}
