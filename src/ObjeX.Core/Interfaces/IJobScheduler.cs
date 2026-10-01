namespace ObjeX.Core.Interfaces;

/// <summary>Error is set when the cron or the time zone is invalid, or the cron never runs. NextRun is UTC.</summary>
public record JobScheduleCheck(string? Error, DateTime? NextRun);

public interface IJobScheduler
{
    /// <summary>A five-field cron in an IANA or Windows time zone, checked the way Hangfire reads it.</summary>
    JobScheduleCheck Check(string cron, string timeZone);
}
