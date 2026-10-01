using Hangfire;
using Microsoft.EntityFrameworkCore;
using ObjeX.Core.Interfaces;
using ObjeX.Core.Models;
using ObjeX.Infrastructure.Data;

namespace ObjeX.Api.Jobs;

/// <summary>
/// Stores the Admin's schedules and applies them to Hangfire. A disabled job keeps its recurring entry with a
/// cron that never fires, so its last run stays visible and Run now still works.
/// </summary>
public class HangfireJobScheduler(IDbContextFactory<ObjeXDbContext> dbFactory, IRecurringJobManager manager) : IJobScheduler
{
    public JobScheduleCheck Check(string cron, string timeZone) => JobCron.Check(cron, timeZone, DateTime.UtcNow);

    public async Task SaveAsync(JobScheduleChange change, string auditUserId, CancellationToken ctk = default)
    {
        var definition = JobDefinitions.Find(change.JobId) ?? throw new ArgumentException($"Unknown job \"{change.JobId}\".", nameof(change));
        if (Check(change.Cron, change.TimeZone).Error is { } error)
            throw new ArgumentException(error, nameof(change));

        await using var db = await dbFactory.CreateDbContextAsync(ctk);
        var row = await db.JobSchedules.FindAsync([change.JobId], ctk);
        var before = row is null ? Describe(definition.DefaultCron, "UTC", true) : Describe(row.Cron, row.TimeZone, row.Enabled);

        if (row is null)
            db.JobSchedules.Add(row = new JobSchedule { JobId = change.JobId, Cron = change.Cron, TimeZone = change.TimeZone, Enabled = change.Enabled });
        else
        {
            row.Cron = change.Cron;
            row.TimeZone = change.TimeZone;
            row.Enabled = change.Enabled;
            row.UpdatedAt = DateTime.UtcNow;
        }

        db.AuditEntries.Add(new AuditEntry
        {
            UserId = auditUserId,
            Action = "UpdateJobSchedule",
            Details = $"{change.JobId}: {before} → {Describe(row.Cron, row.TimeZone, row.Enabled)}",
        });
        await db.SaveChangesAsync(ctk);

        Apply(manager, definition, row);
    }

    /// <summary>The stored schedule, or the default in UTC without one.</summary>
    public static void Apply(IRecurringJobManager manager, JobDefinition definition, JobSchedule? schedule)
    {
        var cron = schedule is null ? definition.DefaultCron : schedule.Enabled ? schedule.Cron : Cron.Never();
        var zone = schedule is null ? TimeZoneInfo.Utc : TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZone);
        manager.AddOrUpdate(definition.Id, definition.Job, cron, new RecurringJobOptions { TimeZone = zone });
    }

    private static string Describe(string cron, string timeZone, bool enabled) => $"{cron} {timeZone}, {(enabled ? "enabled" : "disabled")}";
}
