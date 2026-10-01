using Hangfire;
using Microsoft.EntityFrameworkCore;
using ObjeX.Core.Interfaces;
using ObjeX.Core.Models;
using ObjeX.Infrastructure.Data;

namespace ObjeX.Api.Jobs;

public sealed record EffectiveSchedule(string Cron, TimeZoneInfo Zone, string? Warning);

/// <summary>
/// Stores the Admin's schedules and applies them to Hangfire. A disabled job keeps its recurring entry with a
/// cron that never fires, so its last run stays visible and Run now still works.
/// </summary>
public class HangfireJobScheduler(
    IDbContextFactory<ObjeXDbContext> dbFactory,
    IRecurringJobManager manager,
    ILogger<HangfireJobScheduler> logger) : IJobScheduler
{
    public JobScheduleCheck Check(string cron, string timeZone) => JobCron.Check(cron, timeZone, DateTime.UtcNow);

    public async Task SaveAsync(JobScheduleChange change, string auditUserId, CancellationToken ctk = default)
    {
        var definition = Find(change.JobId);
        if (Check(change.Cron, change.TimeZone).Error is { } error)
            throw new ArgumentException(error, nameof(change));
        if (change.Parameter is { } value)
        {
            var p = definition.Parameter ?? throw new ArgumentException($"{definition.Name} has no setting.", nameof(change));
            if (value < p.Min || value > p.Max)
                throw new ArgumentException($"{p.Label} must be between {p.Min} and {p.Max} {p.Unit}.", nameof(change));
        }

        await using var db = await dbFactory.CreateDbContextAsync(ctk);
        var settings = await db.SystemSettings.SingleAsync(ctk);
        var row = await db.JobSchedules.FindAsync([change.JobId], ctk);
        var before = Describe(definition, row, settings);

        if (row is null)
            db.JobSchedules.Add(row = new JobSchedule { JobId = change.JobId, Cron = change.Cron, TimeZone = change.TimeZone, Enabled = change.Enabled });
        else
        {
            row.Cron = change.Cron;
            row.TimeZone = change.TimeZone;
            row.Enabled = change.Enabled;
            row.UpdatedAt = DateTime.UtcNow;
        }
        definition.Parameter?.Set(settings, change.Parameter == definition.Parameter.Default ? null : change.Parameter);

        db.AuditEntries.Add(new AuditEntry
        {
            UserId = auditUserId,
            Action = "UpdateJobSchedule",
            Details = $"{change.JobId}: {before} → {Describe(definition, row, settings)}",
        });
        await db.SaveChangesAsync(ctk);

        Apply(manager, definition, row, logger);
    }

    public async Task ResetAsync(string jobId, string auditUserId, CancellationToken ctk = default)
    {
        var definition = Find(jobId);

        await using var db = await dbFactory.CreateDbContextAsync(ctk);
        var settings = await db.SystemSettings.SingleAsync(ctk);
        var row = await db.JobSchedules.FindAsync([jobId], ctk);
        var before = Describe(definition, row, settings);

        if (row is not null)
            db.JobSchedules.Remove(row);
        definition.Parameter?.Set(settings, null);

        db.AuditEntries.Add(new AuditEntry
        {
            UserId = auditUserId,
            Action = "ResetJobSchedule",
            Details = $"{jobId}: {before} → {Describe(definition, null, settings)}",
        });
        await db.SaveChangesAsync(ctk);

        Apply(manager, definition, null, logger);
    }

    /// <summary>The stored schedule, or the default in UTC without one. A disabled job keeps its entry with a cron that never fires.</summary>
    public static void Apply(IRecurringJobManager manager, JobDefinition definition, JobSchedule? schedule, ILogger logger)
    {
        var effective = Resolve(definition, schedule);
        if (effective.Warning is { } warning)
            logger.LogWarning("Job '{JobId}': {Warning}", definition.Id, warning);

        var cron = schedule is { Enabled: false } ? Cron.Never() : effective.Cron;
        manager.AddOrUpdate(definition.Id, definition.Job, cron, new RecurringJobOptions { TimeZone = effective.Zone });
    }

    /// <summary>
    /// The schedule Hangfire runs. A row from another host or edited by hand may hold a zone this host lacks or a cron
    /// Hangfire refuses; the job then runs in UTC or on its default, and Warning says so.
    /// </summary>
    public static EffectiveSchedule Resolve(JobDefinition definition, JobSchedule? schedule)
    {
        if (schedule is null)
            return new(definition.DefaultCron, TimeZoneInfo.Utc, null);

        if (JobCron.Check(schedule.Cron, "UTC", DateTime.UtcNow).Error is not null)
            return new(definition.DefaultCron, TimeZoneInfo.Utc,
                $"The stored schedule \"{schedule.Cron}\" is invalid. The job runs on its default schedule.");

        return TimeZoneInfo.TryFindSystemTimeZoneById(schedule.TimeZone, out var zone)
            ? new(schedule.Cron, zone, null)
            : new(schedule.Cron, TimeZoneInfo.Utc,
                $"This server does not know the time zone {schedule.TimeZone}. The job runs its schedule in UTC.");
    }

    private static JobDefinition Find(string jobId)
        => JobDefinitions.Find(jobId) ?? throw new ArgumentException($"Unknown job \"{jobId}\".", nameof(jobId));

    private static string Describe(JobDefinition definition, JobSchedule? schedule, SystemSettings settings)
    {
        var text = schedule is null
            ? $"{definition.DefaultCron} UTC, enabled"
            : $"{schedule.Cron} {schedule.TimeZone}, {(schedule.Enabled ? "enabled" : "disabled")}";
        return definition.Parameter is { } p
            ? $"{text}, {p.Label.ToLowerInvariant()} {p.Get(settings) ?? p.Default} {p.Unit}"
            : text;
    }
}
