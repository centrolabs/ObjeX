using Cronos;
using ObjeX.Core.Interfaces;

namespace ObjeX.Api.Jobs;

/// <summary>Cron checks with Cronos, the parser Hangfire embeds. Five fields only; Hangfire would also take seconds.</summary>
public static class JobCron
{
    public static JobScheduleCheck Check(string cron, string timeZone, DateTime nowUtc)
    {
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out var zone))
            return new($"Unknown time zone \"{timeZone}\".", null);

        CronExpression expression;
        try
        {
            expression = CronExpression.Parse(cron, CronFormat.Standard);
        }
        catch (CronFormatException e)
        {
            return new(e.Message, null);
        }

        return expression.GetNextOccurrence(nowUtc, zone) is { } next
            ? new(null, next)
            : new("The expression never runs.", null);
    }
}
