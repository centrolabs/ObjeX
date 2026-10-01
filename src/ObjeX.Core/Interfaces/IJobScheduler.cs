namespace ObjeX.Core.Interfaces;

/// <summary>Error is set when the cron or the time zone is invalid, or the cron never runs. NextRun is UTC.</summary>
public record JobScheduleCheck(string? Error, DateTime? NextRun);

/// <summary>A recurring job's schedule as the Admin sets it. The cron is read in TimeZone; Parameter null is the job's default.</summary>
public record JobScheduleChange(string JobId, bool Enabled, string Cron, string TimeZone, int? Parameter = null);

public interface IJobScheduler
{
    /// <summary>A five-field cron in an IANA or Windows time zone, checked the way Hangfire reads it.</summary>
    JobScheduleCheck Check(string cron, string timeZone);

    /// <summary>Stores the schedule with an audit entry and applies it to Hangfire. ArgumentException for an unknown job, an invalid schedule or a parameter out of range.</summary>
    Task SaveAsync(JobScheduleChange change, string auditUserId, CancellationToken ctk = default);

    /// <summary>Removes the stored schedule and parameter with an audit entry; the job runs on its defaults in UTC again.</summary>
    Task ResetAsync(string jobId, string auditUserId, CancellationToken ctk = default);
}
