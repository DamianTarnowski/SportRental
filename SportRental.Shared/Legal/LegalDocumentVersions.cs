namespace SportRental.Shared.Legal;

/// <summary>
/// Publiczne wersje dokumentów prawnych obowiązujących w aplikacji klienckiej.
/// Zmiana treści wymagająca ponownej akceptacji musi otrzymać nową wersję.
/// </summary>
public static class LegalDocumentVersions
{
    public const string Terms = "2026-07-10";
    public const string Privacy = "2026-07-10";

    public static DateTime EffectiveFromUtc { get; } =
        new(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc);
}
