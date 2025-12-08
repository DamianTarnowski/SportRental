using Microsoft.Extensions.Configuration;
using Npgsql;
using Spectre.Console;
using System.Text;

// 🎯 SportRental Database Tool
// Bezpieczne narzędzie do przeglądania bazy danych PostgreSQL

// Wczytaj konfigurację z appsettings.json / appsettings.Development.json
var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile("appsettings.Development.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var connectionStrings = new Dictionary<string, string>();
var connSection = configuration.GetSection("ConnectionStrings");
foreach (var child in connSection.GetChildren())
{
    if (!string.IsNullOrEmpty(child.Value) && !child.Value.StartsWith("<"))
    {
        connectionStrings[child.Key] = child.Value;
    }
}

if (connectionStrings.Count == 0)
{
    AnsiConsole.MarkupLine("[red]❌ Brak connection stringów![/]");
    AnsiConsole.MarkupLine("[yellow]Skopiuj appsettings.json do appsettings.Development.json i uzupełnij dane.[/]");
    return;
}

var queryHistory = new List<string>();

AnsiConsole.Write(new FigletText("DB Tool").Color(Color.Blue));
AnsiConsole.MarkupLine("[grey]SportRental Database Explorer[/]\n");

// Wybór bazy
var database = AnsiConsole.Prompt(
    new SelectionPrompt<string>()
        .Title("[yellow]Wybierz bazę danych:[/]")
        .AddChoices(connectionStrings.Keys));

var connectionString = connectionStrings[database];

// Testuj połączenie
AnsiConsole.Status()
    .Start($"[yellow]Łączę z bazą {database}...[/]", ctx =>
    {
        using var conn = new NpgsqlConnection(connectionString);
        conn.Open();
        AnsiConsole.MarkupLine($"[green]✓[/] Połączono z bazą: [cyan]{conn.Database}[/]");
    });

// Główna pętla menu
while (true)
{
    var choice = AnsiConsole.Prompt(
        new SelectionPrompt<string>()
            .Title("\n[yellow]Co chcesz zrobić?[/]")
            .AddChoices(
                "📋 Lista tabel",
                "🔍 Wykonaj SQL query",
                "📊 Szybkie statystyki",
                "📜 Historia zapytań",
                "💾 Eksportuj do CSV",
                "🔄 Zmień bazę danych",
                "❌ Wyjście"
            ));

    if (choice == "❌ Wyjście")
    {
        AnsiConsole.MarkupLine("\n[grey]Do zobaczenia! 👋[/]");
        break;
    }

    try
    {
        using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        switch (choice)
        {
            case "📋 Lista tabel":
                await ShowTables(conn);
                break;

            case "🔍 Wykonaj SQL query":
                await ExecuteQuery(conn, queryHistory);
                break;

            case "📊 Szybkie statystyki":
                await ShowStatistics(conn);
                break;

            case "📜 Historia zapytań":
                ShowHistory(queryHistory);
                break;

            case "💾 Eksportuj do CSV":
                await ExportToCsv(conn, queryHistory);
                break;

            case "🔄 Zmień bazę danych":
                database = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("[yellow]Wybierz nową bazę:[/]")
                        .AddChoices(connectionStrings.Keys));
                connectionString = connectionStrings[database];
                AnsiConsole.MarkupLine($"[green]✓[/] Przełączono na: [cyan]{database}[/]");
                break;
        }
    }
    catch (Exception ex)
    {
        AnsiConsole.WriteException(ex);
    }
}

// ========== FUNKCJE ==========

static async Task ShowTables(NpgsqlConnection conn)
{
    var sql = @"
        SELECT table_name, 
               (SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = 'public' AND table_name = t.table_name) as column_count
        FROM information_schema.tables t
        WHERE table_schema = 'public'
        ORDER BY table_name";

    using var cmd = new NpgsqlCommand(sql, conn);
    using var reader = await cmd.ExecuteReaderAsync();

    var table = new Table()
        .Border(TableBorder.Rounded)
        .AddColumn("[yellow]Tabela[/]")
        .AddColumn("[yellow]Kolumny[/]");

    int count = 0;
    while (await reader.ReadAsync())
    {
        count++;
        table.AddRow(
            $"[cyan]{reader.GetString(0)}[/]",
            reader.GetInt64(1).ToString()
        );
    }

    AnsiConsole.WriteLine();
    AnsiConsole.Write(table);
    AnsiConsole.MarkupLine($"\n[green]Znaleziono {count} tabel[/]");
}

static async Task ExecuteQuery(NpgsqlConnection conn, List<string> history)
{
    var sql = AnsiConsole.Prompt(
        new TextPrompt<string>("[yellow]Wpisz SQL query (SELECT):[/]")
            .PromptStyle("green")
            .ValidationErrorMessage("[red]Musisz wpisać query[/]")
            .Validate(query =>
            {
                if (string.IsNullOrWhiteSpace(query))
                    return ValidationResult.Error("[red]Query nie może być pusty[/]");

                if (!query.Trim().ToUpperInvariant().StartsWith("SELECT"))
                    return ValidationResult.Error("[red]Tylko SELECT queries są dozwolone[/]");

                return ValidationResult.Success();
            }));

    history.Add($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {sql}");

    await AnsiConsole.Status()
        .StartAsync("[yellow]Wykonuję query...[/]", async ctx =>
        {
            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.CommandTimeout = 30;
            using var reader = await cmd.ExecuteReaderAsync();

            // Kolumny
            var columns = new List<string>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                columns.Add(reader.GetName(i));
            }

            // Tabela wyników
            var table = new Table().Border(TableBorder.Rounded);
            foreach (var col in columns)
            {
                table.AddColumn($"[yellow]{col}[/]");
            }

            // Wiersze
            int rowCount = 0;
            int maxRows = 100; // Limit dla bezpieczeństwa

            while (await reader.ReadAsync() && rowCount < maxRows)
            {
                var values = new string[reader.FieldCount];
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    values[i] = reader.IsDBNull(i) ? "[grey]NULL[/]" : reader.GetValue(i)?.ToString() ?? "";
                }
                table.AddRow(values);
                rowCount++;
            }

            AnsiConsole.WriteLine();
            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine($"\n[green]Wyświetlono {rowCount} wierszy[/]" + 
                (rowCount >= maxRows ? " [yellow](limit 100)[/]" : ""));
        });
}

static async Task ShowStatistics(NpgsqlConnection conn)
{
    var queries = new Dictionary<string, string>
    {
        ["Produkty"] = "SELECT COUNT(*) FROM \"Products\"",
        ["Tenanci"] = "SELECT COUNT(*) FROM \"Tenants\"",
        ["Klienci"] = "SELECT COUNT(*) FROM \"Customers\"",
        ["Wynajmy"] = "SELECT COUNT(*) FROM \"Rentals\"",
        ["Aktywne Holds"] = "SELECT COUNT(*) FROM \"ReservationHolds\" WHERE \"ExpiresAtUtc\" > NOW()"
    };

    var table = new Table()
        .Border(TableBorder.Rounded)
        .AddColumn("[yellow]Tabela[/]")
        .AddColumn("[yellow]Liczba[/]");

    foreach (var (label, sql) in queries)
    {
        try
        {
            using var cmd = new NpgsqlCommand(sql, conn);
            var result = await cmd.ExecuteScalarAsync();
            table.AddRow($"[cyan]{label}[/]", $"[green]{result}[/]");
        }
        catch
        {
            table.AddRow($"[cyan]{label}[/]", "[red]Błąd[/]");
        }
    }

    AnsiConsole.WriteLine();
    AnsiConsole.Write(table);
}

static void ShowHistory(List<string> history)
{
    if (history.Count == 0)
    {
        AnsiConsole.MarkupLine("\n[yellow]Brak historii zapytań[/]");
        return;
    }

    AnsiConsole.WriteLine();
    foreach (var entry in history.TakeLast(20))
    {
        AnsiConsole.MarkupLine($"[grey]{entry}[/]");
    }
    AnsiConsole.MarkupLine($"\n[green]Wyświetlono {Math.Min(20, history.Count)} z {history.Count} zapytań[/]");
}

static async Task ExportToCsv(NpgsqlConnection conn, List<string> history)
{
    if (history.Count == 0)
    {
        AnsiConsole.MarkupLine("\n[yellow]Brak historii - najpierw wykonaj jakieś query[/]");
        return;
    }

    // Pokaż ostatnie query z historii
    var lastQuery = history.Last().Split('|')[1].Trim();
    AnsiConsole.MarkupLine($"\n[grey]Ostatnie query:[/] [cyan]{lastQuery}[/]");

    if (!AnsiConsole.Confirm("Eksportować wyniki tego query do CSV?"))
        return;

    var fileName = $"export_{DateTime.Now:yyyyMMdd_HHmmss}.csv";

    await AnsiConsole.Status()
        .StartAsync("[yellow]Eksportuję do CSV...[/]", async ctx =>
        {
            using var cmd = new NpgsqlCommand(lastQuery, conn);
            using var reader = await cmd.ExecuteReaderAsync();

            var csv = new StringBuilder();

            // Nagłówki
            var headers = new List<string>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                headers.Add(reader.GetName(i));
            }
            csv.AppendLine(string.Join(",", headers.Select(h => $"\"{h}\"")));

            // Dane
            int rowCount = 0;
            while (await reader.ReadAsync())
            {
                var values = new List<string>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    var value = reader.IsDBNull(i) ? "" : reader.GetValue(i)?.ToString() ?? "";
                    values.Add($"\"{value.Replace("\"", "\"\"")}\""); // Escape cudzysłowów
                }
                csv.AppendLine(string.Join(",", values));
                rowCount++;
            }

            await File.WriteAllTextAsync(fileName, csv.ToString());
            AnsiConsole.MarkupLine($"\n[green]✓ Wyeksportowano {rowCount} wierszy do:[/] [cyan]{fileName}[/]");
        });
}
