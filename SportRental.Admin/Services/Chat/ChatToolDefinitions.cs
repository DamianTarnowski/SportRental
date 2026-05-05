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
            """))),

        ChatTool.CreateFunctionTool(
            functionName: "search_rentals",
            functionDescription:
                "Wyszukuje wynajmy po wielu kryteriach. Wszystkie parametry opcjonalne — możesz zawęzić " +
                "po kliencie (nazwisko / email / telefon), statusie (Active, Confirmed, Completed, " +
                "Cancelled, Draft), oknie czasowym (days_ahead = StartDate w ciągu N dni od teraz, " +
                "days_behind = EndDate nie starszy niż N dni). Zwraca max 15 wynajmów posortowanych " +
                "od najnowszego StartDate. Używaj gdy user pyta 'pokaż mi wynajmy klienta X', " +
                "'co miałem w zeszłym tygodniu?', 'pokaż mi szkice wynajmów'.",
            functionParameters: BinaryData.FromBytes(System.Text.Encoding.UTF8.GetBytes("""
            {
              "type": "object",
              "properties": {
                "customer_query": { "type": "string", "description": "Fragment imienia, email lub telefonu klienta." },
                "status": { "type": "string", "enum": ["Draft", "Confirmed", "Active", "Completed", "Cancelled"] },
                "days_ahead": { "type": "integer", "description": "StartDate w ciągu N dni od teraz." },
                "days_behind": { "type": "integer", "description": "EndDate nie starszy niż N dni." }
              },
              "required": []
            }
            """))),

        ChatTool.CreateFunctionTool(
            functionName: "get_overdue_rentals",
            functionDescription:
                "Zwraca wynajmy zaległe — sprzęt który powinien być już zwrócony (EndDate w przeszłości) " +
                "ale klient go jeszcze nie oddał, ani wynajem nie został anulowany. KRYTYCZNE — " +
                "to potencjalnie utracone $ albo zguby. Pokazuje klienta, telefon, ile dni po terminie. " +
                "Używaj gdy user pyta 'kto nie oddał?', 'jakie mam zaległości?', 'kogo wezwać?'.",
            functionParameters: BinaryData.FromBytes(System.Text.Encoding.UTF8.GetBytes("""
            {
              "type": "object",
              "properties": {},
              "required": []
            }
            """))),

        ChatTool.CreateFunctionTool(
            functionName: "get_pending_actions",
            functionDescription:
                "Co użytkownik ma do zrobienia DZIŚ. Zwraca: ile wynajmów do wydania dziś, do zwrotu dziś, " +
                "ile szkiców (draft) do dokończenia, ile niepotwierdzonych SMS-ów u klientów, ile zaległych. " +
                "Używaj gdy user pyta 'co robić?', 'co mam dziś?', 'jakie zadania na teraz?'.",
            functionParameters: BinaryData.FromBytes(System.Text.Encoding.UTF8.GetBytes("""
            {
              "type": "object",
              "properties": {},
              "required": []
            }
            """))),

        ChatTool.CreateFunctionTool(
            functionName: "get_revenue_summary",
            functionDescription:
                "Przychody z wynajmów za podany okres. Zwraca liczbę wynajmów, łączną kwotę, ile " +
                "zakończonych, ile w toku, średnią wartość. Używaj gdy user pyta 'ile zarobiłem?', " +
                "'jaki przychód w tym miesiącu?', 'przychód za ostatni tydzień'.",
            functionParameters: BinaryData.FromBytes(System.Text.Encoding.UTF8.GetBytes("""
            {
              "type": "object",
              "properties": {
                "period": {
                  "type": "string",
                  "enum": ["today", "week", "month", "year"],
                  "description": "Okres: today=dzisiaj, week=ost. 7 dni, month=ost. 30 dni, year=ost. 365 dni."
                }
              },
              "required": []
            }
            """))),

        ChatTool.CreateFunctionTool(
            functionName: "get_customer_history",
            functionDescription:
                "Pełna historia klienta — wszystkie wynajmy + trust level + dane kontaktowe. " +
                "Akceptuje email, telefon lub fragment imienia. Zwraca dane klienta + max 15 ostatnich " +
                "wynajmów. Używaj gdy user pyta 'historia klienta X', 'co kiedyś brał Y?', " +
                "'pokaż wynajmy Damiana'.",
            functionParameters: BinaryData.FromBytes(System.Text.Encoding.UTF8.GetBytes("""
            {
              "type": "object",
              "properties": {
                "query": { "type": "string", "description": "Email, telefon albo imię/nazwisko klienta." }
              },
              "required": ["query"]
            }
            """))),

        ChatTool.CreateFunctionTool(
            functionName: "find_rental_by_sku",
            functionDescription:
                "Znajdź aktywny wynajem powiązany z konkretnym sprzętem po jego SKU/kodzie kreskowym. " +
                "Idealne gdy user mówi 'kto ma teraz ROW-001?' albo zeskanował kod sprzętu i chce wiedzieć " +
                "z kim to jest. Zwraca produkt + wszystkie aktywne wynajmy które go zawierają.",
            functionParameters: BinaryData.FromBytes(System.Text.Encoding.UTF8.GetBytes("""
            {
              "type": "object",
              "properties": {
                "sku": { "type": "string", "description": "SKU/kod kreskowy sprzętu, dokładny." }
              },
              "required": ["sku"]
            }
            """)))
    };
}
