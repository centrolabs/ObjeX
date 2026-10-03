namespace ObjeX.Web.Helpers;

public static class RelativeTime
{
    /// <summary>After a week a count of days says less than the timestamp itself.</summary>
    public static readonly TimeSpan Limit = TimeSpan.FromDays(7);

    /// <summary>
    /// "just now", "12 min ago", "5 hours ago", "3 days ago", or "in 12 min" for a time ahead. Null from a week on,
    /// where the caller shows the full timestamp. Counts are rounded down, so "1 hour ago" means at least one full hour.
    /// </summary>
    public static string? Format(DateTime utc, DateTime nowUtc)
    {
        var delta = nowUtc - DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        var ahead = delta < TimeSpan.Zero;
        var span = ahead ? delta.Negate() : delta;

        if (span >= Limit) return null;
        if (span < TimeSpan.FromMinutes(1)) return "just now";

        var amount = span < TimeSpan.FromHours(1) ? $"{(int)span.TotalMinutes} min"
            : span < TimeSpan.FromDays(1) ? Plural((int)span.TotalHours, "hour")
            : Plural((int)span.TotalDays, "day");
        return ahead ? $"in {amount}" : $"{amount} ago";
    }

    /// <summary>Whether the text can still change: within a week of now, ahead or behind. After that it is a fixed timestamp.</summary>
    public static bool Changes(DateTime utc, DateTime nowUtc) =>
        (nowUtc - DateTime.SpecifyKind(utc, DateTimeKind.Utc)).Duration() < Limit;

    static string Plural(int count, string unit) => count == 1 ? $"1 {unit}" : $"{count} {unit}s";
}
