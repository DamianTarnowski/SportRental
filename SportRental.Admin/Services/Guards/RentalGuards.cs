using Microsoft.EntityFrameworkCore;
using SportRental.Infrastructure.Data;
using SportRental.Infrastructure.Domain;

namespace SportRental.Admin.Services.Guards;

/// Centralne reguły biznesowe pilnujące właściciela przed pomyłkami (Maciej Blokady).
/// Używane zarówno przed save'em rental (conflict guard) jak i przed wydaniem.
public static class RentalGuards
{
    /// Zwraca powód blokady wydania lub null gdy można wydać sprzęt.
    public static string? GetIssueBlockReason(Rental rental)
    {
        if (string.IsNullOrEmpty(rental.ContractUrl))
            return "Brak wygenerowanej umowy — wygeneruj umowę przed wydaniem.";
        if (!IsRentalPaid(rental))
            return $"Brak płatności — status: {(string.IsNullOrEmpty(rental.PaymentStatus) ? "nie pobrano" : rental.PaymentStatus)}.";
        if (rental.Status == RentalStatus.Cancelled)
            return "Wynajem jest anulowany.";
        if (rental.Status == RentalStatus.Completed || rental.IssuedAtUtc.HasValue)
            return "Wynajem był już wydany.";
        return null;
    }

    public static bool CanIssue(Rental rental) => GetIssueBlockReason(rental) is null;

    /// PaymentStatus pochodzi z kilku źródeł (Stripe, demo, manualny), więc whitelista
    /// wartości potwierdzających płatność. Pusty string lub negatywne wartości = NOT PAID.
    public static bool IsRentalPaid(Rental rental)
    {
        var status = rental.PaymentStatus?.Trim() ?? string.Empty;
        return status is "DepositPaid" or "succeeded" or "paid" or "Paid";
    }

    /// Sprawdza overlap dla pary (productId, [start,end]) wśród aktywnych wynajmów tego tenantu.
    /// Zwraca null gdy nie ma kolizji, lub powód z numerem konfliktowego wynajmu.
    public static async Task<string?> GetReservationConflictAsync(
        ApplicationDbContext db,
        Guid tenantId,
        Guid productId,
        DateTime startUtc,
        DateTime endUtc,
        Guid? excludeRentalId = null,
        CancellationToken ct = default)
    {
        if (endUtc <= startUtc) return "Data końca musi być po dacie początku.";

        // Aktywne wynajmy = wszystko poza Cancelled/Completed.
        // UWAGA: NIE używamy `new[]{...}.Contains(x)` — w .NET 9/10 rezolwuje to do
        // `MemoryExtensions.Contains(ReadOnlySpan<T>, T)`, a EF interpreter próbuje
        // skompilować ReadOnlySpan<T> jako generic argument (ref struct) → runtime
        // TypeLoadException. Rzutujemy więc na konkretne sprawdzenie OR per status.
        var excludeId = excludeRentalId ?? Guid.Empty;

        var query = db.Rentals
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(r => r.TenantId == tenantId
                && (r.Status == RentalStatus.Draft
                    || r.Status == RentalStatus.Pending
                    || r.Status == RentalStatus.Confirmed
                    || r.Status == RentalStatus.Active)
                && r.Items.Any(i => i.ProductId == productId)
                && r.StartDateUtc < endUtc
                && r.EndDateUtc > startUtc);

        if (excludeId != Guid.Empty)
            query = query.Where(r => r.Id != excludeId);

        var conflicting = await query
            .Select(r => new { r.Id, r.StartDateUtc, r.EndDateUtc })
            .FirstOrDefaultAsync(ct);

        if (conflicting == null) return null;

        var shortId = conflicting.Id.ToString()[..8].ToUpper();
        return $"Egzemplarz jest zarezerwowany przez wynajem #{shortId} " +
               $"({conflicting.StartDateUtc.ToLocalTime():dd.MM HH:mm} – {conflicting.EndDateUtc.ToLocalTime():dd.MM HH:mm}).";
    }
}
