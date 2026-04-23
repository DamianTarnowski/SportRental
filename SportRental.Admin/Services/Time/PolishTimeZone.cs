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
        public static readonly TimeZoneInfo Instance = Resolve();

        private static TimeZoneInfo Resolve()
        {
            if (TimeZoneInfo.TryFindSystemTimeZoneById("Europe/Warsaw", out var tz))
            {
                return tz;
            }
            if (TimeZoneInfo.TryFindSystemTimeZoneById("Central European Standard Time", out var tzWin))
            {
                return tzWin;
            }
            // Awaryjnie — stała UTC+1 (bez DST). Lepsze niż rzucenie wyjątkiem w serwisie powiadomień.
            return TimeZoneInfo.CreateCustomTimeZone("PL-Fallback", TimeSpan.FromHours(1), "PL-Fallback", "PL-Fallback");
        }

        public static DateTime FromUtc(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(
            utc.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(utc, DateTimeKind.Utc) : utc.ToUniversalTime(),
            Instance);
    }
}
