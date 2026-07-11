using SportRental.Shared.Models;
using SportRental.Shared.Formatting;

namespace SportRental.Client.Helpers;

internal static class RentalPresentation
{
    private static readonly HashSet<string> PaidStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "DepositPaid",
        "Succeeded",
        "Paid"
    };

    public static bool IsPaymentConfirmed(MyRentalDto rental) =>
        PaidStatuses.Contains(rental.PaymentStatus?.Trim() ?? string.Empty);

    public static decimal PaidAmount(MyRentalDto rental)
    {
        if (rental.PaidAmount > 0m)
            return Math.Min(rental.TotalAmount, rental.PaidAmount);

        // Fallback dla rekordów sprzed migracji jawnego PaidAmount. DepositPaid
        // oznacza zwrotną kaucję, a nie opłaconą cenę wynajmu.
        var status = rental.PaymentStatus?.Trim() ?? string.Empty;
        return status is "Succeeded" or "succeeded" or "Paid" or "paid"
            ? Math.Max(0m, rental.TotalAmount)
            : 0m;
    }

    public static decimal OutstandingAmount(MyRentalDto rental)
    {
        var rentalFeeOutstanding = Math.Max(0m, rental.TotalAmount - PaidAmount(rental));
        var damageOutstanding = Math.Max(
            0m,
            Math.Max(0m, rental.DamageCharge ?? 0m) - RetainedDeposit(rental));
        return rentalFeeOutstanding + damageOutstanding;
    }

    private static decimal RetainedDeposit(MyRentalDto rental)
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

    public static bool IsDepositCollected(MyRentalDto rental) =>
        rental.DepositAmount > 0m &&
        !rental.ReturnDepositRefund.HasValue &&
        (rental.DepositPaidAtUtc.HasValue ||
         string.Equals(rental.PaymentStatus?.Trim(), "DepositPaid", StringComparison.OrdinalIgnoreCase));

    public static string RentalTypeText(MyRentalDto rental) =>
        rental.RentalType == RentalTypeDto.Hourly ? "Wynajem godzinowy" : "Wynajem dzienny";

    public static string DurationText(MyRentalDto rental)
    {
        if (rental.RentalType == RentalTypeDto.Hourly)
        {
            var hours = rental.HoursRented ?? Math.Max(1, (int)Math.Ceiling((rental.EndDateUtc - rental.StartDateUtc).TotalHours));
            return $"{hours} {PolishPlural(hours, "godzina", "godziny", "godzin")}";
        }

        var days = Math.Max(1, (int)Math.Ceiling((rental.EndDateUtc - rental.StartDateUtc).TotalDays));
        return $"{days} {PolishPlural(days, "dzień", "dni", "dni")}";
    }

    public static string StatusText(string? status) => (status ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "draft" => "Szkic",
        "pending" => "Oczekujące",
        "confirmed" => "Potwierdzone",
        "active" => "Aktywne",
        "completed" => "Zakończone",
        "cancelled" or "canceled" => "Anulowane",
        // Odczyt starszych rekordów; decyzje funkcjonalne nie opierają się na tych statusach.
        "reserved" => "Zarezerwowane",
        "onrent" => "Aktywne",
        "returned" => "Zakończone",
        "overdue" => "Po terminie",
        "" => "Brak statusu",
        _ => status!
    };

    public static string StatusCssClass(string? status) => (status ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "draft" or "pending" => "pending",
        "confirmed" or "reserved" => "reserved",
        "active" or "onrent" => "active",
        "completed" or "returned" => "completed",
        "cancelled" or "canceled" => "cancelled",
        "overdue" => "overdue",
        _ => string.Empty
    };

    public static string PaymentStatusText(MyRentalDto rental)
    {
        var status = rental.PaymentStatus?.Trim() ?? string.Empty;
        if (string.Equals(status, "DepositPaid", StringComparison.OrdinalIgnoreCase))
            return "Depozyt opłacony";
        if (status is "Succeeded" or "succeeded" or "Paid" or "paid")
            return "Opłacono w całości";

        return status.ToLowerInvariant() switch
        {
            "pending" or "processing" or "requires_confirmation" or "requires_action" => "Płatność w toku",
            "requires_payment_method" => "Oczekuje na płatność",
            "failed" => "Płatność nieudana",
            "depositrefunded" => "Depozyt zwrócony",
            "depositpartiallyrefunded" => "Depozyt częściowo zwrócony",
            "depositretained" => "Depozyt zatrzymany przy rozliczeniu",
            "refunded" => "Płatność zwrócona",
            "canceled" or "cancelled" => "Płatność anulowana",
            "" => "Brak potwierdzonej płatności",
            _ => status
        };
    }

    public static string PaymentMethodText(string? method) => (method ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "online" => "Online",
        "card" => "Karta",
        "blik" => "BLIK",
        "cash" => "Gotówka",
        "transfer" => "Przelew",
        "" => "Nie podano",
        _ => method!
    };

    public static bool CanReview(MyRentalDto rental) =>
        !rental.HasReview && string.Equals(rental.Status, "Completed", StringComparison.OrdinalIgnoreCase);

    public static string Money(decimal value) => value.Pln();

    public static string PickupLocation(MyRentalDto rental)
    {
        var address = rental.PickupAddress?.Trim();
        var city = rental.PickupCity?.Trim();
        if (string.IsNullOrWhiteSpace(address))
            return city ?? string.Empty;
        if (string.IsNullOrWhiteSpace(city) ||
            address.Contains(city, StringComparison.CurrentCultureIgnoreCase))
        {
            return address;
        }

        return $"{address}, {city}";
    }

    public static string? DepositSettlementText(MyRentalDto rental)
    {
        var status = rental.PaymentStatus?.Trim() ?? string.Empty;
        var refunded = rental.ReturnDepositRefund;
        if (!refunded.HasValue &&
            (string.Equals(status, "DepositRefunded", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(status, "Refunded", StringComparison.OrdinalIgnoreCase)))
        {
            refunded = rental.DepositAmount;
        }

        if (refunded.HasValue)
        {
            var returned = Math.Clamp(refunded.Value, 0m, rental.DepositAmount);
            var retained = Math.Max(0m, rental.DepositAmount - returned);
            if (returned > 0m && retained > 0m)
                return $"Depozyt: zwrócono {Money(returned)}, zatrzymano {Money(retained)}";
            if (returned > 0m)
                return $"Depozyt zwrócony: {Money(returned)}";
            if (rental.DepositAmount > 0m)
                return $"Depozyt zatrzymany: {Money(rental.DepositAmount)}";
        }

        return string.Equals(status, "DepositRetained", StringComparison.OrdinalIgnoreCase)
            ? $"Depozyt zatrzymany: {Money(rental.DepositAmount)}"
            : null;
    }

    private static string PolishPlural(int value, string singular, string paucal, string plural)
    {
        var abs = Math.Abs(value);
        if (abs == 1)
            return singular;
        if (abs % 100 is >= 12 and <= 14)
            return plural;
        return abs % 10 is >= 2 and <= 4 ? paucal : plural;
    }
}
