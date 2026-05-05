using OpenAI.Chat;

namespace SportRental.Admin.Services.Chat;

/// <summary>
/// Statyczne definicje tools dla floating chat:
///  - Phase1Tools: write tools (report_bug, submit_feedback)
///  - Phase2Tools: read tools (get_today_rentals, get_product_status, get_customer_trust, count_active_rentals)
///  - All: oba zestawy razem — używane przez UI w produkcji.
/// </summary>
public static class ChatToolDefinitions
{
    public static IReadOnlyList<ChatTool> All => Phase1Tools.Concat(Phase2Tools).ToArray();

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

    public static IReadOnlyList<ChatTool> Phase2Tools { get; } = new[]
    {
        ChatTool.CreateFunctionTool(
            functionName: "get_today_rentals",
            functionDescription:
                "Zwraca listę wynajmów na dziś — czyli aktywnych rezerwacji których czas trwa, " +
                "kończy się dziś, lub zaczyna się dziś. Używaj gdy użytkownik pyta " +
                "'co mam dziś do wydania?', 'kto przychodzi po sprzęt?', 'jakie wynajmy są w toku?'. " +
                "Zwraca max 20 wynajmów posortowanych po dacie końca.",
            functionParameters: BinaryData.FromBytes(System.Text.Encoding.UTF8.GetBytes("""
            {
              "type": "object",
              "properties": {},
              "required": []
            }
            """))),

        ChatTool.CreateFunctionTool(
            functionName: "get_product_status",
            functionDescription:
                "Sprawdza dostępność i szczegóły produktu (sprzętu) w katalogu. Akceptuje SKU " +
                "(dokładny kod kreskowy/QR sprzętu) ALBO fragment nazwy produktu. Zwraca max 5 dopasowań " +
                "z liczbą dostępnych sztuk, ceną, kategorią. Używaj gdy user pyta 'czy jest rower X?', " +
                "'ile mam sztuk SKU ROW-001?', 'pokaż mi narty'.",
            functionParameters: BinaryData.FromBytes(System.Text.Encoding.UTF8.GetBytes("""
            {
              "type": "object",
              "properties": {
                "sku_or_name": {
                  "type": "string",
                  "description": "SKU produktu albo fragment nazwy do wyszukania."
                }
              },
              "required": ["sku_or_name"]
            }
            """))),

        ChatTool.CreateFunctionTool(
            functionName: "get_customer_trust",
            functionDescription:
                "Pobiera poziom zaufania klienta (Verified / NoIssues / Watch / Restricted) " +
                "obliczany z ocen ze wszystkich wypożyczalni. Akceptuje email, telefon lub fragment imienia/nazwiska. " +
                "Zwraca max 3 dopasowania ze statystykami (liczba wynajmów, średnia ocen, incydenty). " +
                "Używaj gdy user pyta 'jaki ma trust ten klient?', 'czy klient X ma jakieś flagi?'.",
            functionParameters: BinaryData.FromBytes(System.Text.Encoding.UTF8.GetBytes("""
            {
              "type": "object",
              "properties": {
                "query": {
                  "type": "string",
                  "description": "Email, telefon albo fragment imienia/nazwiska klienta."
                }
              },
              "required": ["query"]
            }
            """))),

        ChatTool.CreateFunctionTool(
            functionName: "count_active_rentals",
            functionDescription:
                "Zwraca szybkie statystyki wynajmów dla mojego tenanta: ile aktywnych, " +
                "ile draft, ile zakończonych dziś, ile zaczyna się dziś, ile w tym tygodniu. " +
                "Używaj gdy user pyta 'ile mam wynajmów?', 'jaki ruch w tym tygodniu?', 'jaki status?'.",
            functionParameters: BinaryData.FromBytes(System.Text.Encoding.UTF8.GetBytes("""
            {
              "type": "object",
              "properties": {},
              "required": []
            }
            """)))
    };
}
