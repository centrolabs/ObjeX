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

/// <summary>One step in the life of a run: Enqueued, Processing, Succeeded, Failed, Scheduled (a retry) or Deleted.
/// Data holds what Hangfire recorded for the step, for example the server, the latency or the exception type.</summary>
public record JobStateChange(string State, DateTime At, string? Reason, IReadOnlyDictionary<string, string> Data);

/// <summary>Everything the Jobs page shows for one run. Times are UTC.</summary>
public record JobRunDetails(
    JobRun Run,
    string? Method,
    DateTime? CreatedAt,
    DateTime? FinishedAt,
    string? Server,
    string? RawResult,
    string? ExceptionType,
    string? ExceptionDetails,
    IReadOnlyList<JobStateChange> History);

public interface IJobMonitor
{
    IReadOnlyList<RecurringJobStatus> GetRecurringJobs();

    /// <summary>Processing, retrying, succeeded and failed runs, newest first.</summary>
    IReadOnlyList<JobRun> GetRecentRuns(int count);

    /// <summary>Enqueues a run of the recurring job now; its schedule stays as it is.</summary>
    void Trigger(string recurringJobId);

    /// <summary>The run with its state history, or null when Hangfire no longer holds it.</summary>
    JobRunDetails? GetRun(string jobId);

    /// <summary>Enqueues a failed or retrying run again. False when the run no longer exists.</summary>
    bool Retry(string jobId);

    /// <summary>Moves a run that is not running to Deleted. False when the run no longer exists.</summary>
    bool Delete(string jobId);
}
