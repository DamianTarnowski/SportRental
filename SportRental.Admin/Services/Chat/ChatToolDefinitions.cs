using OpenAI.Chat;

namespace SportRental.Admin.Services.Chat;

/// <summary>
/// Statyczne definicje tools dla floating chat. Faza 1: tylko write tools dla feedbacku
/// i błędów. Faza 2 dorzuci read-only tools dla danych domeny (rentals, products).
/// </summary>
public static class ChatToolDefinitions
{
    public static IReadOnlyList<ChatTool> Phase1Tools { get; } = new[]
    {
        ChatTool.CreateFunctionTool(
            functionName: "report_bug",
            functionDescription:
                "Zapisuje zgłoszenie błędu od użytkownika do systemu. Używaj gdy użytkownik mówi że coś nie działa, " +
                "widzi błąd, wyjątek, dziwne zachowanie. Zbierz szczegóły zanim wywołasz: co próbował zrobić, " +
                "co się stało, jaki komunikat zobaczył. Po zapisie potwierdź że zgłoszenie trafiło do zespołu.",
            functionParameters: BinaryData.FromBytes(System.Text.Encoding.UTF8.GetBytes("""
            {
              "type": "object",
              "properties": {
                "message": {
                  "type": "string",
                  "description": "Opis błędu z perspektywy użytkownika — krótki ale konkretny."
                },
                "severity": {
                  "type": "string",
                  "enum": ["low", "medium", "high"],
                  "description": "low = drobny UX, medium = niedogodność, high = blokuje pracę / utracone dane."
                }
              },
              "required": ["message"]
            }
            """))),

        ChatTool.CreateFunctionTool(
            functionName: "submit_feedback",
            functionDescription:
                "Zapisuje ogólny feedback użytkownika — pochwałę, sugestię, pomysł, pytanie. NIE używaj dla błędów " +
                "(do tego użyj report_bug). Po zapisie podziękuj i potwierdź że zespół to zobaczy.",
            functionParameters: BinaryData.FromBytes(System.Text.Encoding.UTF8.GetBytes("""
            {
              "type": "object",
              "properties": {
                "message": {
                  "type": "string",
                  "description": "Treść feedbacku użytkownika."
                },
                "type": {
                  "type": "string",
                  "enum": ["general", "suggestion", "question", "praise"],
                  "description": "Kategoria: general = ogólnie, suggestion = sugestia ulepszenia, question = pytanie, praise = pochwała."
                }
              },
              "required": ["message"]
            }
            """)))
    };
}
