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
        {
            var paidAmount = GetPaidAmount(rental);
            return paidAmount > 0m
                ? $"Pozostało do zapłaty {GetOutstandingAmount(rental):0.00} zł."
                : $"Brak płatności — status: {(string.IsNullOrEmpty(rental.PaymentStatus) ? "nie pobrano" : rental.PaymentStatus)}.";
        }
        if (rental.Status == RentalStatus.Cancelled)
            return "Wynajem jest anulowany.";
        if (rental.Status == RentalStatus.Completed || rental.IssuedAtUtc.HasValue)
            return "Wynajem był już wydany.";
        return null;
    }

    public static bool CanIssue(Rental rental) => GetIssueBlockReason(rental) is null;

    /// Zwraca faktycznie zaksięgowaną kwotę. PaidAmount jest źródłem prawdy dla nowych
    /// rekordów; fallback utrzymuje poprawny odczyt danych sprzed migracji.
    public static bool IsRentalPaid(Rental rental)
    {
        return rental.TotalAmount > 0m && GetPaidAmount(rental) >= rental.TotalAmount;
    }

    public static bool HasAnyPayment(Rental rental) => GetPaidAmount(rental) > 0m;

    public static decimal GetOutstandingAmount(Rental rental)
    {
        var rentalFeeOutstanding = Math.Max(0m, rental.TotalAmount - GetPaidAmount(rental));
        var damageOutstanding = Math.Max(
            0m,
            Math.Max(0m, rental.DamageCharge ?? 0m) - GetRetainedDeposit(rental));
        return rentalFeeOutstanding + damageOutstanding;
    }

    private static decimal GetRetainedDeposit(Rental rental)
    {
        if (!rental.ReturnDepositRefund.HasValue)
            return 0m;

        var status = rental.PaymentStatus?.Trim() ?? string.Empty;
        var wasCollected = rental.DepositPaidAtUtc.HasValue ||
                           string.Equals(status, "DepositPaid", StringComparison.OrdinalIgnoreCase) ||
                           rental.ReturnDepositRefund.Value > 0m;
        return wasCollected
            ? Math.Max(0m, rental.DepositAmount - Math.Max(0m, rental.ReturnDepositRefund.Value))
            : 0m;
    }

    public static decimal GetPaidAmount(Rental rental)
    {
        var status = rental.PaymentStatus?.Trim() ?? string.Empty;
        if (status is "Refunded" or "refunded" or "DepositRefunded" or "depositrefunded")
            return 0m;

        if (rental.PaidAmount > 0m)
            return Math.Min(Math.Max(0m, rental.TotalAmount), rental.PaidAmount);

        return status is "succeeded" or "Succeeded" or "paid" or "Paid"
            ? Math.Max(0m, rental.TotalAmount)
            : 0m;
    }

    public static bool IsDepositCollected(Rental rental) =>
        rental.DepositAmount > 0m &&
        !rental.ReturnDepositRefund.HasValue &&
        !string.Equals(rental.PaymentStatus?.Trim(), "DepositRefunded", StringComparison.OrdinalIgnoreCase) &&
        (rental.DepositPaidAtUtc.HasValue ||
         string.Equals(rental.PaymentStatus?.Trim(), "DepositPaid", StringComparison.OrdinalIgnoreCase));

    /// Sprawdza ilościową dostępność produktu w zadanym terminie.
    /// Zwraca null, gdy żądana ilość mieści się w stanie po odjęciu rezerwacji,
    /// albo czytelny powód blokady. Przy edycji można wykluczyć bieżący wynajem.
    public static async Task<string?> GetReservationConflictAsync(
        ApplicationDbContext db,
        Guid tenantId,
        Guid productId,
        int requestedQuantity,
        DateTime startUtc,
        DateTime endUtc,
        Guid? excludeRentalId = null,
        CancellationToken ct = default)
    {
        if (endUtc <= startUtc) return "Data końca musi być po dacie początku.";
        if (requestedQuantity <= 0) return "Ilość sprzętu musi być większa od zera.";

        var product = await db.Products
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(candidate => candidate.Id == productId && candidate.TenantId == tenantId)
            .Select(candidate => new { candidate.Name, candidate.AvailableQuantity })
            .SingleOrDefaultAsync(ct);

        if (product is null)
            return "Produkt nie istnieje albo nie należy do tej wypożyczalni.";

        var excludeId = excludeRentalId ?? Guid.Empty;

        var blockingRentals = db.Rentals
            .AsNoTracking()
            .IgnoreQueryFilters()
            .WhereInventoryBlocking()
            .Where(rental => rental.TenantId == tenantId
                && rental.StartDateUtc < endUtc
                && rental.EndDateUtc > startUtc);

        if (excludeId != Guid.Empty)
            blockingRentals = blockingRentals.Where(rental => rental.Id != excludeId);

        var reservedQuantity = await db.RentalItems
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(item => item.ProductId == productId)
            .Join(
                blockingRentals,
                item => item.RentalId,
                rental => rental.Id,
                (item, rental) => item.Quantity)
            .SumAsync(quantity => (int?)quantity, ct) ?? 0;

        var availableQuantity = Math.Max(0, product.AvailableQuantity - reservedQuantity);
        if (requestedQuantity <= availableQuantity)
            return null;

        return $"Brak wystarczającej liczby sztuk produktu „{product.Name}”. " +
               $"Dostępne: {availableQuantity}, wybrano: {requestedQuantity}.";
    }
}
