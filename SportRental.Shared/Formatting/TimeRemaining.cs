namespace SportRental.Shared.Formatting;

/// Formatowanie pozostałego/przekroczonego czasu w polskim ("zostało 2h 15min" / "spóźniony 30min").
public static class TimeRemaining
{
    public static string Format(DateTime targetUtc) => Format(targetUtc, DateTime.UtcNow);

    public static string Format(DateTime targetUtc, DateTime referenceUtc)
    {
        var diff = targetUtc - referenceUtc;
        var minutes = (int)Math.Round(diff.TotalMinutes);
        if (minutes == 0) return "teraz";
        var overdue = minutes < 0;
        minutes = Math.Abs(minutes);

        string body;
        if (minutes < 60) body = $"{minutes} min";
        else if (minutes < 60 * 24)
        {
            var h = minutes / 60;
            var m = minutes % 60;
            body = m > 0 ? $"{h}h {m}min" : $"{h}h";
        }
        else
        {
            var d = minutes / (60 * 24);
            var h = (minutes % (60 * 24)) / 60;
            body = h > 0 ? $"{d}d {h}h" : $"{d}d";
        }

        return overdue ? $"spóźniony {body}" : $"zostało {body}";
    }
}
