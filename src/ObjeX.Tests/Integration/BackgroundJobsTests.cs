using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Hangfire.Storage;
using Microsoft.Extensions.DependencyInjection;
using ObjeX.Api.Startup;
using Microsoft.EntityFrameworkCore;
using ObjeX.Core.Interfaces;
using ObjeX.Core.Models;
using ObjeX.Infrastructure.Data;
using ObjeX.Infrastructure.Jobs;

namespace ObjeX.Tests.Integration;

public class BackgroundJobsTests(ObjeXFactory factory) : IClassFixture<ObjeXFactory>
{
    [Fact]
    public void RegisterRecurringJobs_RemovesJobsThisVersionDoesNotDeclare()
    {
        // Simulate a recurring job left in storage by an earlier version of the code.
        var manager = factory.Services.GetRequiredService<IRecurringJobManager>();
        manager.AddOrUpdate("stale-from-old-version", () => Console.WriteLine("stale"), Cron.Never());

        BackgroundJobs.RegisterRecurringJobs(factory.Services);

        using var connection = factory.Services.GetRequiredService<JobStorage>().GetConnection();
        var ids = connection.GetRecurringJobs().Select(j => j.Id).Order().ToArray();

        Assert.Equal(["cleanup-abandoned-multipart", "cleanup-orphaned-blobs", "recount-bucket-stats", "verify-blob-integrity"], ids);
    }

    [Fact]
    public void SucceededRuns_StayInStorageFor30Days()
    {
        var storage = factory.Services.GetRequiredService<JobStorage>();
        var id = new BackgroundJobClient(storage, new JobFilterCollection()).Create(
            Job.FromExpression<CleanupOrphanedBlobsJob>(j => j.ExecuteAsync()), new SucceededState(null, 0, 0));

        var expireAt = storage.GetMonitoringApi().JobDetails(id).ExpireAt;

        var expected = DateTime.UtcNow.AddDays(30);
        Assert.InRange(expireAt!.Value, expected.AddMinutes(-5), expected.AddMinutes(5));
    }

    [Fact]
    public async Task RegisterRecurringJobs_KeepsADisabledJobDisabled()
    {
        await factory.Services.GetRequiredService<IJobScheduler>()
            .SaveAsync(new JobScheduleChange("cleanup-abandoned-multipart", false, "0 5 * * 0", "UTC"), "test");

        BackgroundJobs.RegisterRecurringJobs(factory.Services);

        using var connection = factory.Services.GetRequiredService<JobStorage>().GetConnection();
        var job = Assert.Single(connection.GetRecurringJobs(), j => j.Id == "cleanup-abandoned-multipart");
        Assert.Null(job.NextExecution);
    }

    [Fact]
    public async Task AfterARestart_TheStoredScheduleIsActive()
    {
        using var first = new ObjeXFactory();
        await first.Services.GetRequiredService<IJobScheduler>()
            .SaveAsync(new JobScheduleChange("verify-blob-integrity", true, "15 1 * * 2", "Europe/Zurich"), "test");

        using var second = first.Restart();
        using var connection = second.Services.GetRequiredService<JobStorage>().GetConnection();
        var job = Assert.Single(connection.GetRecurringJobs(), j => j.Id == "verify-blob-integrity");

        Assert.Equal("15 1 * * 2", job.Cron);
        Assert.Equal("Europe/Zurich", job.TimeZoneId);
    }

    [Fact]
    public async Task OnAHostThatDoesNotKnowTheStoredZone_TheAppStarts_AndRunsTheCronInUtc()
    {
        using var first = new ObjeXFactory();
        await StoreAsync(first, new JobSchedule { JobId = "verify-blob-integrity", Cron = "15 1 * * 2", TimeZone = "Mars/Olympus_Mons" });

        using var second = first.Restart();
        var jobs = RecurringJobs(second);

        Assert.Equal(("15 1 * * 2", "UTC"), (jobs["verify-blob-integrity"].Cron, jobs["verify-blob-integrity"].TimeZoneId));
        Assert.Equal(4, jobs.Count);
    }

    [Fact]
    public async Task WithAStoredCronHangfireCannotRead_TheAppStarts_AndTheJobRunsOnItsDefault()
    {
        using var first = new ObjeXFactory();
        await StoreAsync(first, new JobSchedule { JobId = "cleanup-orphaned-blobs", Cron = "61 * * * *", TimeZone = "Europe/Zurich" });

        using var second = first.Restart();
        var jobs = RecurringJobs(second);

        Assert.Equal(("0 3 * * 0", "UTC"), (jobs["cleanup-orphaned-blobs"].Cron, jobs["cleanup-orphaned-blobs"].TimeZoneId));
        Assert.Equal(4, jobs.Count);
    }

    // Written past the scheduler's checks, as a database moved from another host or edited by hand would hold it.
    private static async Task StoreAsync(ObjeXFactory host, JobSchedule schedule)
    {
        await using var db = await host.Services.GetRequiredService<IDbContextFactory<ObjeXDbContext>>().CreateDbContextAsync();
        db.JobSchedules.Add(schedule);
        await db.SaveChangesAsync();
    }

    private static Dictionary<string, RecurringJobDto> RecurringJobs(ObjeXFactory host)
    {
        using var connection = host.Services.GetRequiredService<JobStorage>().GetConnection();
        return connection.GetRecurringJobs().ToDictionary(j => j.Id);
    }
}
