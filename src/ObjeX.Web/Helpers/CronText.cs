using System.Globalization;
using ObjeX.Web.Services;

namespace ObjeX.Web.Helpers;

public static class CronText
{
    /// <summary>
    /// "Every Sunday at 05:00" for a weekly cron, "Every day at 05:00" for a daily one, null for anything else.
    /// Day and time come from the next run in the browser's zone, so a zone shift or daylight saving time is
    /// already applied; without a next run they come from the cron and carry the zone it is read in.
    /// </summary>
    public static string? Describe(string cron, DateTime? nextRunUtc, BrowserTimeZone tz, string cronZone = "UTC")
    {
        var fields = cron.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields is not [var minute, var hour, "*", "*", var weekday]
            || !int.TryParse(minute, out var m) || !int.TryParse(hour, out var h))
            return null;

        int? day = weekday == "*" ? null : int.TryParse(weekday, out var d) && d is >= 0 and <= 7 ? d % 7 : -1;
        if (day == -1)
            return null;

        if (nextRunUtc is { } next)
        {
            var local = tz.ToLocal(next);
            var at = tz.FormatClock(TimeOnly.FromDateTime(local));
            return day is null ? $"Every day at {at}" : $"Every {local.DayOfWeek} at {at}";
        }

        var clock = h is >= 0 and <= 23 && m is >= 0 and <= 59 ? tz.FormatClock(new TimeOnly(h, m)) : $"{h:00}:{m:00}";
        var time = cronZone == tz.Zone.Id ? clock : $"{clock} {cronZone}";
        return day is null ? $"Every day at {time}" : $"Every {(DayOfWeek)day} at {time}";
    }
}
