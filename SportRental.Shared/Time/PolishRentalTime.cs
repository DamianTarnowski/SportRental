namespace SportRental.Shared.Time;

/// <summary>
/// Converts wall-clock values selected in a client <c>datetime-local</c> field
/// as Europe/Warsaw time. The conversion must not depend on the device or host
/// operating-system time zone.
/// </summary>
public static class PolishRentalTime
{
    /// <summary>
    /// Small lead time that prevents a rental from starting while its request is
    /// still being validated and persisted.
    /// </summary>
    public static readonly TimeSpan MinimumLeadTime = TimeSpan.FromMinutes(2);

    public static readonly TimeZoneInfo Zone = ResolveZone();

    public static DateTime NowLocal => FromUtc(DateTime.UtcNow);

    public static DateTime TodayLocal => NowLocal.Date;

    /// <summary>
    /// Treats all date/time fields as a Polish wall-clock value, regardless of
    /// their <see cref="DateTime.Kind"/>. During the repeated autumn hour the
    /// later (standard-time) occurrence is chosen, so a rental never starts
    /// earlier than the customer-selected wall-clock time suggests.
    /// </summary>
    public static DateTime ToUtc(DateTime polishLocal)
    {
        var wallClock = DateTime.SpecifyKind(polishLocal, DateTimeKind.Unspecified);
        if (Zone.IsInvalidTime(wallClock))
        {
            throw new ArgumentException(
                "Wybrana godzina nie istnieje z powodu zmiany czasu. Wybierz inną godzinę.",
                nameof(polishLocal));
        }

        if (Zone.IsAmbiguousTime(wallClock))
        {
            var standardOffset = Zone.GetAmbiguousTimeOffsets(wallClock).Min();
            return new DateTimeOffset(wallClock, standardOffset).UtcDateTime;
        }

        return TimeZoneInfo.ConvertTimeToUtc(wallClock, Zone);
    }

    public static bool TryToUtc(DateTime polishLocal, out DateTime utc)
    {
        try
        {
            utc = ToUtc(polishLocal);
            return true;
        }
        catch (ArgumentException)
        {
            utc = default;
            return false;
        }
    }

    public static DateTime FromUtc(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(NormalizeUtc(utc), Zone);

    public static bool IsStartSafelyInFuture(
        DateTime startUtc,
        DateTime nowUtc,
        TimeSpan? minimumLeadTime = null) =>
        NormalizeUtc(startUtc) >= NormalizeUtc(nowUtc).Add(minimumLeadTime ?? MinimumLeadTime);

    /// <summary>
    /// Earliest value suitable for an HTML datetime-local input. It is rounded
    /// up to a full minute because the controls used by the Client do not expose
    /// seconds.
    /// </summary>
    public static DateTime EarliestStartLocal(DateTime? nowUtc = null)
    {
        var local = FromUtc((nowUtc ?? DateTime.UtcNow).Add(MinimumLeadTime));
        var remainder = local.Ticks % TimeSpan.TicksPerMinute;
        return remainder == 0
            ? local
            : local.AddTicks(TimeSpan.TicksPerMinute - remainder);
    }

    public static DateTime NormalizeUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        _ => value.ToUniversalTime()
    };

    private static TimeZoneInfo ResolveZone()
    {
        if (TimeZoneInfo.TryFindSystemTimeZoneById("Europe/Warsaw", out var iana))
            return iana;
        if (TimeZoneInfo.TryFindSystemTimeZoneById("Central European Standard Time", out var windows))
            return windows;

        // Last-resort fallback for a stripped runtime without time-zone data.
        return TimeZoneInfo.CreateCustomTimeZone(
            "PL-Fallback",
            TimeSpan.FromHours(1),
            "PL-Fallback",
            "PL-Fallback");
    }
}
