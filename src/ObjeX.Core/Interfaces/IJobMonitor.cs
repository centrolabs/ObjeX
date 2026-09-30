namespace ObjeX.Core.Interfaces;

/// <summary>Retrying: the run failed and waits for its next automatic attempt.</summary>
public enum JobRunState { Queued, Processing, Succeeded, Failed, Retrying, Other }

/// <summary>One run of a background job. Result is the job's return value in words; Error the exception message or retry reason.</summary>
public record JobRun(
    string Id,
    string Name,
    JobRunState State,
    DateTime? StartedAt,
    TimeSpan? Duration,
    string? Result,
    string? Error);

/// <summary>A recurring job with its schedule and its most recent run. Times are UTC.</summary>
public record RecurringJobStatus(
    string Id,
    string Name,
    string Cron,
    DateTime? NextRun,
    DateTime? LastRun,
    JobRun? LastJob);

public interface IJobMonitor
{
    IReadOnlyList<RecurringJobStatus> GetRecurringJobs();

    /// <summary>Processing, succeeded and failed runs, newest first.</summary>
    IReadOnlyList<JobRun> GetRecentRuns(int count);

    /// <summary>Enqueues a run of the recurring job now; its schedule stays as it is.</summary>
    void Trigger(string recurringJobId);
}
