using SportRental.Shared.Time;

namespace SportRental.Admin.Services.Time
{
    /// <summary>
    /// Strefa czasowa dla Polski. Azure App Service oraz większość Linux hostów
    /// chodzą w UTC — `DateTime.ToLocalTime()` daje wtedy UTC (no-op), więc klient
    /// dostaje mail/SMS z błędną godziną. Każdy user-facing timestamp idzie przez
    /// tę klasę. ID "Europe/Warsaw" działa na Linux+ICU; "Central European Standard Time"
    /// to fallback dla Windows/starych hostów.
    /// </summary>
    public static class PolishTimeZone
    {
        public static readonly TimeZoneInfo Instance = PolishRentalTime.Zone;

        public static DateTime FromUtc(DateTime utc) => PolishRentalTime.FromUtc(utc);
    }
}
