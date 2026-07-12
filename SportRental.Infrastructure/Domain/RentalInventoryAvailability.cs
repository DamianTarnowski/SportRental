using System.Linq.Expressions;

namespace SportRental.Infrastructure.Domain;

/// <summary>
/// Jedno źródło prawdy dla statusów wynajmu, które blokują sprzęt w wybranym
/// przedziale czasu. Rekordy zakończone i anulowane pozostają w historii, ale
/// nie pomniejszają bieżącej dostępności.
/// </summary>
public static class RentalInventoryAvailability
{
    private static readonly Expression<Func<Rental, bool>> InventoryBlockingExpression =
        rental => rental.ReturnedAtUtc == null &&
                  (rental.Status == RentalStatus.Draft ||
                   rental.Status == RentalStatus.Pending ||
                   rental.Status == RentalStatus.Confirmed ||
                   rental.Status == RentalStatus.Active);

    /// <summary>
    /// Predykat przeznaczony do zapytań EF Core. Jawny łańcuch OR jest celowy:
    /// użycie tablicy z Contains powoduje TypeLoadException w używanej wersji EF.
    /// </summary>
    public static Expression<Func<Rental, bool>> BlocksInventoryExpression =>
        InventoryBlockingExpression;

    public static bool BlocksInventory(Rental rental)
    {
        ArgumentNullException.ThrowIfNull(rental);
        return BlocksInventory(rental.Status, rental.ReturnedAtUtc);
    }

    public static bool BlocksInventory(RentalStatus status, DateTime? returnedAtUtc = null) =>
        returnedAtUtc is null &&
        status is RentalStatus.Draft
            or RentalStatus.Pending
            or RentalStatus.Confirmed
            or RentalStatus.Active;

    /// <summary>
    /// Ogranicza zapytanie do wynajmów, które faktycznie rezerwują stan.
    /// Metoda zachowuje pełne rekordy historyczne w bazie.
    /// </summary>
    public static IQueryable<Rental> WhereInventoryBlocking(this IQueryable<Rental> rentals)
    {
        ArgumentNullException.ThrowIfNull(rentals);
        return rentals.Where(InventoryBlockingExpression);
    }
}
