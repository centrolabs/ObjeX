namespace ObjeX.Web.Helpers;

public enum JobFrequency { Daily, Weekly, Custom }

public record JobPreset(JobFrequency Frequency, DayOfWeek Day, TimeOnly Time);

/// <summary>The daily and weekly presets of the job dialog and their cron. Anything else is a custom cron.</summary>
public static class CronPreset
{
    private static readonly JobPreset Custom = new(JobFrequency.Custom, DayOfWeek.Sunday, TimeOnly.MinValue);

    public static string ToCron(JobPreset preset) => preset.Frequency switch
    {
        JobFrequency.Daily => $"{preset.Time.Minute} {preset.Time.Hour} * * *",
        JobFrequency.Weekly => $"{preset.Time.Minute} {preset.Time.Hour} * * {(int)preset.Day}",
        _ => throw new ArgumentOutOfRangeException(nameof(preset), "A custom cron has no preset."),
    };

    /// <summary>
    /// Day and time come from the next run in the browser's zone when there is one, so a cron read in another zone
    /// shows the local time it fires at; without a next run they come from the cron itself.
    /// </summary>
    public static JobPreset Read(string cron, DateTime? nextLocal)
    {
        var fields = cron.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields is not [var minute, var hour, "*", "*", var weekday]
            || !int.TryParse(minute, out var m) || m is < 0 or > 59
            || !int.TryParse(hour, out var h) || h is < 0 or > 23)
            return Custom;

        int? day = weekday == "*" ? null : int.TryParse(weekday, out var d) && d is >= 0 and <= 7 ? d % 7 : -1;
        if (day == -1)
            return Custom;

        var frequency = day is null ? JobFrequency.Daily : JobFrequency.Weekly;
        return nextLocal is { } next
            ? new(frequency, next.DayOfWeek, new TimeOnly(next.Hour, next.Minute))
            : new(frequency, (DayOfWeek)(day ?? 0), new TimeOnly(h, m));
    }
}
