using System.Globalization;

namespace SportRental.Shared.Formatting;

/// Globalne formatowanie PLN — zawsze "35,00 zł" (przecinek, separator spacja, suffix zł).
/// Używaj jako `decimal.Pln()` w Razor + serwisach zamiast `.ToString("C")` (ten gubi
/// formatowanie zależnie od kultury runtime'u, w Linuxowym kontenerze App Service zwraca "¤35.00").
public static class Money
{
    private static readonly CultureInfo PolishCulture = new("pl-PL");

    public static string Pln(this decimal value)
        => value.ToString("N2", PolishCulture) + " zł";

    public static string Pln(this decimal? value)
        => value.HasValue ? value.Value.Pln() : "—";

    /// Bez groszy ("35 zł") gdy chcemy oszczędzić miejsca na małym chipie.
    public static string PlnInt(this decimal value)
        => Math.Round(value).ToString("N0", PolishCulture) + " zł";
}
