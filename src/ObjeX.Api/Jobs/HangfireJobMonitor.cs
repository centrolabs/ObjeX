using Hangfire;
using Hangfire.Common;
using Hangfire.Storage;
using Hangfire.Storage.Monitoring;
using Microsoft.EntityFrameworkCore;
using ObjeX.Core.Interfaces;
using ObjeX.Infrastructure.Data;
using ObjeX.Infrastructure.Jobs;

namespace ObjeX.Api.Jobs;

/// <summary>
/// Reads recurring jobs and runs from Hangfire storage for the Jobs page. Every run is read from its state
/// history, whose data the Hangfire state classes write themselves; the StateData of the list DTOs depends on
/// the storage (null on SQLite).
/// </summary>
public class HangfireJobMonitor(
    JobStorage storage,
    IRecurringJobManager manager,
    IBackgroundJobClient client,
    IDbContextFactory<ObjeXDbContext> dbFactory) : IJobMonitor
{
    public IReadOnlyList<RecurringJobStatus> GetRecurringJobs()
    {
        var monitor = storage.GetMonitoringApi();
        using var connection = storage.GetConnection();
        using var db = dbFactory.CreateDbContext();
        var entries = connection.GetRecurringJobs().ToDictionary(j => j.Id);
        var schedules = db.JobSchedules.AsNoTracking().ToDictionary(s => s.JobId);
        var settings = db.SystemSettings.AsNoTracking().Single();

        return JobDefinitions.All
            .Select(d =>
            {
                var entry = entries.GetValueOrDefault(d.Id);
                var schedule = schedules.GetValueOrDefault(d.Id);
                var enabled = schedule?.Enabled ?? true;
                var parameter = d.Parameter?.Read(settings);
                var effective = HangfireJobScheduler.Resolve(d, schedule);
                return new RecurringJobStatus(
                    d.Id,
                    d.Name,
                    effective.Cron,
                    effective.Zone.Id,
                    enabled,
                    schedule is null && parameter?.Value is null,
                    enabled ? entry?.NextExecution : null,
                    entry?.LastExecution,
                    entry?.LastJobId is { Length: > 0 } lastId ? ReadRun(monitor, lastId) : null,
                    parameter,
                    effective.Warning);
            })
            .ToList();
    }

    public IReadOnlyList<JobRun> GetRecentRuns(int count)
    {
        var monitor = storage.GetMonitoringApi();

        var processing = monitor.ProcessingJobs(0, count).Where(j => j.Value is { InProcessingState: true }).Select(j => (j.Key, At: j.Value!.StartedAt));
        var succeeded = monitor.SucceededJobs(0, count).Where(j => j.Value is { InSucceededState: true }).Select(j => (j.Key, At: j.Value!.SucceededAt));
        var failed = monitor.FailedJobs(0, count).Where(j => j.Value is { InFailedState: true }).Select(j => (j.Key, At: j.Value!.FailedAt));
        // AutomaticRetry parks a failed attempt in Scheduled; without these the run would vanish until its last attempt.
        var retrying = monitor.ScheduledJobs(0, count).Where(j => j.Value is { InScheduledState: true }).Select(j => (j.Key, At: j.Value!.ScheduledAt));

        return processing.Concat(succeeded).Concat(failed).Concat(retrying)
            .OrderByDescending(j => j.At ?? DateTime.MinValue)
            .Take(count)
            .Select(j => ReadRun(monitor, j.Key))
            .OfType<JobRun>()
            .OrderByDescending(r => r.StartedAt ?? DateTime.MinValue)
            .ToList();
    }

    public void Trigger(string recurringJobId) => manager.Trigger(recurringJobId);

    public JobRunDetails? GetRun(string jobId)
    {
        var monitor = storage.GetMonitoringApi();
        if (ReadRun(monitor, jobId) is not { } run || monitor.JobDetails(jobId) is not { } details)
            return null;

        var history = details.History; // newest first
        var failed = history.FirstOrDefault(h => h.StateName == "Failed");
        var finished = history.FirstOrDefault(h => h.StateName is "Succeeded" or "Failed");
        return new JobRunDetails(
            run,
            details.Job is { } job ? $"{job.Type.Name}.{job.Method.Name}" : null,
            // The first state is when the run was created; the DTO's CreatedAt is not reliable on every storage.
            history.Count > 0 ? DateTime.SpecifyKind(history[^1].CreatedAt, DateTimeKind.Utc) : null,
            finished is null ? null : TimeOf(finished, finished.StateName == "Succeeded" ? "SucceededAt" : "FailedAt"),
            history.Select(h => Get(h, "ServerId")).FirstOrDefault(server => server is not null),
            history.Where(h => h.StateName == "Succeeded").Select(h => Get(h, "Result")).FirstOrDefault(),
            failed is null ? null : Get(failed, "ExceptionType"),
            failed is null ? null : Get(failed, "ExceptionDetails"),
            history
                .Select(h => new JobStateChange(h.StateName, DateTime.SpecifyKind(h.CreatedAt, DateTimeKind.Utc), h.Reason,
                    (IReadOnlyDictionary<string, string>?)h.Data ?? new Dictionary<string, string>()))
                .ToList());
    }

    public bool Retry(string jobId) => client.Requeue(jobId);

    public bool Delete(string jobId) => client.Delete(jobId);

    private static JobRun? ReadRun(IMonitoringApi monitor, string jobId)
    {
        var details = monitor.JobDetails(jobId);
        if (details?.History is not { Count: > 0 } history)
            return null; // expired

        var current = history[0]; // newest first
        var processing = history.FirstOrDefault(h => h.StateName == "Processing");
        var startedAt = processing is null ? null : TimeOf(processing, "StartedAt");
        var name = NameOf(details.Job) ?? details.InvocationData?.Type ?? jobId;

        return current.StateName switch
        {
            "Succeeded" => new JobRun(jobId, name, JobRunState.Succeeded, startedAt,
                long.TryParse(Get(current, "PerformanceDuration"), out var ms) ? TimeSpan.FromMilliseconds(ms) : null,
                Describe(ReadResult(details.Job, Get(current, "Result"))), null),
            "Failed" => new JobRun(jobId, name, JobRunState.Failed, startedAt,
                TimeOf(current, "FailedAt") - startedAt, null, Get(current, "ExceptionMessage") ?? current.Reason),
            "Processing" => new JobRun(jobId, name, JobRunState.Processing, startedAt, null, null, null),
            "Enqueued" => new JobRun(jobId, name, JobRunState.Queued, startedAt, null, null, null),
            // AutomaticRetry turns a failed attempt into Scheduled; the reason carries the attempt count and the exception message.
            "Scheduled" => new JobRun(jobId, name, JobRunState.Retrying, startedAt, null, null, current.Reason),
            _ => new JobRun(jobId, name, JobRunState.Other, startedAt, null, null, current.Reason),
        };
    }

    private static DateTime? TimeOf(StateHistoryDto state, string key)
        => JobHelper.DeserializeNullableDateTime(Get(state, key))
           ?? DateTime.SpecifyKind(state.CreatedAt, DateTimeKind.Utc);

    private static string? Get(StateHistoryDto state, string key)
        => state.Data is not null && state.Data.TryGetValue(key, out var value) ? value : null;

    private static string? NameOf(Job? job)
        => job is null ? null : JobDefinitions.Of(job.Type)?.Name ?? job.Type.Name;

    /// <summary>The stored result is JSON; the job method's Task&lt;T&gt; says which record it is.</summary>
    private static object? ReadResult(Job? job, string? json)
    {
        if (string.IsNullOrEmpty(json))
            return null;
        if (job?.Method.ReturnType is not { IsGenericType: true } returnType || returnType.GetGenericTypeDefinition() != typeof(Task<>))
            return json;

        try
        {
            return SerializationHelper.Deserialize(json, returnType.GetGenericArguments()[0], SerializationOption.User);
        }
        catch (Newtonsoft.Json.JsonException)
        {
            return json;
        }
    }

    public static string? Describe(object? result) => result switch
    {
        null => null,
        CleanupResult r => $"{Count(r.FilesChecked, "blob file")} checked, {Count(r.FilesDeleted, "orphan")} deleted",
        IntegrityResult r => $"{Count(r.Checked, "blob")} verified, {r.Corrupted:N0} corrupted, {r.Missing:N0} missing"
            + (r.Skipped > 0 ? $", {r.Skipped:N0} multipart skipped" : ""),
        AbandonedMultipartResult r => $"{Count(r.UploadsChecked, "abandoned upload")} found, {r.UploadsDeleted:N0} deleted",
        _ => result.ToString(),
    };

    private static string Count(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n:N0} {noun}s";
}
