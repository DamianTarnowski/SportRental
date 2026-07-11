using System.Text.Json;
using OpenAI.Chat;

namespace SportRental.Admin.Services.Ai;

/// AI-asystent dodawania produktu: czyta zdjęcia, generuje propozycję name/description/category/price.
/// Właściciel zmienia co chce — to PROPOZYCJA, nie auto-execute. Per global memory:
/// AI write actions są ustawialne; tu zawsze tryb "suggest" — owner zatwierdza w formularzu.
public interface IProductAiAssistant
{
    /// Zwraca propozycję wypełnienia formularza produktu na podstawie 1-N zdjęć.
    /// Każdy element images to surowe bajty pliku (jpg/png/webp).
    Task<ProductSuggestion> GenerateFromImagesAsync(
        IReadOnlyList<(byte[] Data, string ContentType)> images,
        string? userHint = null,
        CancellationToken ct = default);
}

public record ProductSuggestion(
    string Name,
    string Description,
    string Category,
    string? Producer,
    string? Model,
    decimal? DailyPriceLow,
    decimal? DailyPriceHigh,
    decimal? HourlyPriceLow,
    decimal? HourlyPriceHigh,
    string? PricingRationale);

public class ProductAiAssistant : IProductAiAssistant
{
    private readonly AzureOpenAiClientProvider _clientProvider;
    private readonly string _deployment;
    private readonly ILogger<ProductAiAssistant> _logger;

    public ProductAiAssistant(
        AzureOpenAiClientProvider clientProvider,
        IConfiguration config,
        ILogger<ProductAiAssistant> logger)
    {
        _clientProvider = clientProvider;
        _deployment = config["OpenAI:TextDeployment"] ?? "gpt-5.5";
        _logger = logger;
    }

    public async Task<ProductSuggestion> GenerateFromImagesAsync(
        IReadOnlyList<(byte[] Data, string ContentType)> images,
        string? userHint = null,
        CancellationToken ct = default)
    {
        if (images.Count == 0)
            throw new ArgumentException("Co najmniej jedno zdjęcie wymagane.", nameof(images));
        if (images.Count > 6)
            images = images.Take(6).ToList(); // limit kosztu

        var client = _clientProvider.Client
            ?? throw new InvalidOperationException("Asystent AI nie jest skonfigurowany dla tego środowiska.");
        var chat = client.GetChatClient(_deployment);

        var systemPrompt = """
            Jesteś asystentem właściciela wypożyczalni sprzętu sportowego w Polsce.
            Na podstawie zdjęć ZIDENTYFIKUJ sprzęt i wygeneruj propozycję do bazy produktów.

            Wymagania:
            - Nazwa: krótka i konkretna, np. "Rower MTB Trek Marlin 6" lub "Kajak 2-osobowy Pelican Argo 100"
            - Opis: 2-4 zdania marketingowe po polsku, podkreśl typ, przeznaczenie, kluczowe cechy widoczne na zdjęciach
            - Kategoria: jedno z: "Rowery", "Kajaki", "SUP", "Narty", "Snowboard", "Buty", "Kaski", "Akcesoria", "Inne"
            - Producer + Model: tylko gdy WIDAĆ na zdjęciu (etykieta, naklejka, charakterystyczne logo). Inaczej null.
            - Ceny w PLN: dailyPriceLow/High to widełki ceny za dobę. Bazuj na realiach polskiego rynku wynajmu sportowego (2025):
              * Rowery turystyczne/MTB: 40-100 zł/dzień
              * Rowery elektryczne: 100-200 zł/dzień
              * Kajaki: 80-150 zł/dzień
              * SUP-y: 70-130 zł/dzień
              * Narty kompletne: 80-180 zł/dzień
              * Buty/kaski/akcesoria: 15-50 zł/dzień
              * Sprzęt premium (carbon, profesjonalny): +50-100%
            - HourlyPrice: tylko gdy sprzęt nadaje się do wynajmu godzinowego (rowery miejskie, SUP, kajaki). Bazuj 1/8 do 1/5 ceny dziennej.
            - pricingRationale: 1 zdanie WHY tej ceny (np. "Sprzęt średniego segmentu, częsta wypożyczalnia weekendowa").

            Zwróć WYŁĄCZNIE JSON, bez markdown, bez komentarzy:
            {
              "name": "string",
              "description": "string",
              "category": "string",
              "producer": "string lub null",
              "model": "string lub null",
              "dailyPriceLow": number,
              "dailyPriceHigh": number,
              "hourlyPriceLow": number lub null,
              "hourlyPriceHigh": number lub null,
              "pricingRationale": "string"
            }
            """;

        var userParts = new List<ChatMessageContentPart>();

        var userText = string.IsNullOrWhiteSpace(userHint)
            ? "Opisz sprzęt na zdjęciach i zaproponuj cenę."
            : $"Opisz sprzęt na zdjęciach i zaproponuj cenę. Dodatkowy kontekst od właściciela: {userHint}";
        userParts.Add(ChatMessageContentPart.CreateTextPart(userText));

        foreach (var (data, contentType) in images)
        {
            var binaryData = BinaryData.FromBytes(data);
            userParts.Add(ChatMessageContentPart.CreateImagePart(binaryData, contentType));
        }

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(systemPrompt),
            new UserChatMessage(userParts)
        };

        var options = new ChatCompletionOptions
        {
            ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat()
        };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var response = await chat.CompleteChatAsync(messages, options, ct);
        sw.Stop();

        var raw = response.Value.Content[0].Text;
        _logger.LogInformation("ProductAi: {Images} zdjęć → {Bytes} bajtów odp. w {Ms}ms",
            images.Count, raw.Length, sw.ElapsedMilliseconds);

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var r = doc.RootElement;
            return new ProductSuggestion(
                Name: r.GetProperty("name").GetString() ?? "Sprzęt",
                Description: r.GetProperty("description").GetString() ?? "",
                Category: r.GetProperty("category").GetString() ?? "Inne",
                Producer: GetStringOrNull(r, "producer"),
                Model: GetStringOrNull(r, "model"),
                DailyPriceLow: GetDecimalOrNull(r, "dailyPriceLow"),
                DailyPriceHigh: GetDecimalOrNull(r, "dailyPriceHigh"),
                HourlyPriceLow: GetDecimalOrNull(r, "hourlyPriceLow"),
                HourlyPriceHigh: GetDecimalOrNull(r, "hourlyPriceHigh"),
                PricingRationale: GetStringOrNull(r, "pricingRationale"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ProductAi: nie udało się sparsować JSON: {Raw}", raw);
            throw new InvalidOperationException("AI zwróciło nieprawidłową odpowiedź. Spróbuj ponownie.", ex);
        }
    }

    private static string? GetStringOrNull(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el)) return null;
        if (el.ValueKind == JsonValueKind.Null) return null;
        var s = el.GetString();
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    private static decimal? GetDecimalOrNull(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el)) return null;
        if (el.ValueKind == JsonValueKind.Null) return null;
        if (el.ValueKind == JsonValueKind.Number) return el.GetDecimal();
        if (el.ValueKind == JsonValueKind.String && decimal.TryParse(el.GetString(), System.Globalization.CultureInfo.InvariantCulture, out var d))
            return d;
        return null;
    }
}
