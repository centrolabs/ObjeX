using Hangfire;
using Hangfire.Storage;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ObjeX.Core.Interfaces;
using ObjeX.Core.Models;
using ObjeX.Infrastructure.Data;

namespace ObjeX.Tests.Integration;

public class JobSchedulerTests(ObjeXFactory factory) : IClassFixture<ObjeXFactory>
{
    private IJobScheduler Scheduler => factory.Services.GetRequiredService<IJobScheduler>();

    private RecurringJobDto Hangfire(string id)
    {
        using var connection = factory.Services.GetRequiredService<JobStorage>().GetConnection();
        return connection.GetRecurringJobs().Single(j => j.Id == id);
    }

    private async Task<string> AdminIdAsync()
    {
        using var scope = factory.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<UserManager<User>>().FindByNameAsync("admin"))!.Id;
    }

    private async Task<ObjeXDbContext> DbAsync()
        => await factory.Services.GetRequiredService<IDbContextFactory<ObjeXDbContext>>().CreateDbContextAsync();

    [Fact]
    public async Task Save_StoresTheSchedule_AndHangfireRunsItInItsTimeZone()
    {
        await Scheduler.SaveAsync(new JobScheduleChange("verify-blob-integrity", true, "30 2 * * 1", "Europe/Zurich"), await AdminIdAsync());

        await using var db = await DbAsync();
        var row = await db.JobSchedules.SingleAsync(s => s.JobId == "verify-blob-integrity");
        Assert.Equal(("30 2 * * 1", "Europe/Zurich", true), (row.Cron, row.TimeZone, row.Enabled));

        var job = Hangfire("verify-blob-integrity");
        Assert.Equal("30 2 * * 1", job.Cron);
        Assert.Equal("Europe/Zurich", job.TimeZoneId);
    }

    [Fact]
    public async Task Save_WritesAnAuditEntryWithTheOldAndTheNewSchedule()
    {
        await Scheduler.SaveAsync(new JobScheduleChange("cleanup-orphaned-blobs", true, "0 1 * * *", "UTC"), await AdminIdAsync());

        await using var db = await DbAsync();
        var entry = await db.AuditEntries.OrderByDescending(e => e.Id).FirstAsync(e => e.Action == "UpdateJobSchedule");
        Assert.Equal("cleanup-orphaned-blobs: 0 3 * * 0 UTC, enabled → 0 1 * * * UTC, enabled", entry.Details);
    }

    [Theory]
    [InlineData("61 * * * *", "UTC")]
    [InlineData("0 4 * * 0", "Mars/Olympus_Mons")]
    public async Task Save_RejectsAnInvalidSchedule_AndStoresNothing(string cron, string timeZone)
    {
        var before = Hangfire("cleanup-abandoned-multipart").Cron;

        await Assert.ThrowsAsync<ArgumentException>(() =>
            Scheduler.SaveAsync(new JobScheduleChange("cleanup-abandoned-multipart", true, cron, timeZone), "unused"));

        await using var db = await DbAsync();
        Assert.False(await db.JobSchedules.AnyAsync(s => s.JobId == "cleanup-abandoned-multipart"));
        Assert.Equal(before, Hangfire("cleanup-abandoned-multipart").Cron);
    }

    [Fact]
    public async Task Save_RejectsAnUnknownJob()
        => await Assert.ThrowsAsync<ArgumentException>(() =>
            Scheduler.SaveAsync(new JobScheduleChange("no-such-job", true, "0 4 * * 0", "UTC"), "unused"));

    [Fact]
    public async Task Disabling_KeepsTheJobInHangfireWithoutANextRun_AndEnablingSchedulesItAgain()
    {
        var admin = await AdminIdAsync();

        await Scheduler.SaveAsync(new JobScheduleChange("verify-blob-integrity", false, "0 4 * * 0", "UTC"), admin);
        Assert.Null(Hangfire("verify-blob-integrity").NextExecution);

        await Scheduler.SaveAsync(new JobScheduleChange("verify-blob-integrity", true, "0 4 * * 0", "UTC"), admin);
        Assert.Equal("0 4 * * 0", Hangfire("verify-blob-integrity").Cron);
        Assert.NotNull(Hangfire("verify-blob-integrity").NextExecution);
    }

    [Fact]
    public async Task Reset_RemovesTheStoredSchedule_AndRestoresTheDefaultInUtc()
    {
        var admin = await AdminIdAsync();
        await Scheduler.SaveAsync(new JobScheduleChange("cleanup-orphaned-blobs", false, "0 2 * * *", "Europe/Zurich"), admin);

        await Scheduler.ResetAsync("cleanup-orphaned-blobs", admin);

        await using var db = await DbAsync();
        Assert.False(await db.JobSchedules.AnyAsync(s => s.JobId == "cleanup-orphaned-blobs"));
        var entry = await db.AuditEntries.OrderByDescending(e => e.Id).FirstAsync();
        Assert.Equal(("ResetJobSchedule", "cleanup-orphaned-blobs: 0 2 * * * Europe/Zurich, disabled → 0 3 * * 0 UTC, enabled"), (entry.Action, entry.Details));

        var job = Hangfire("cleanup-orphaned-blobs");
        Assert.Equal(("0 3 * * 0", "UTC"), (job.Cron, job.TimeZoneId));
    }
}
