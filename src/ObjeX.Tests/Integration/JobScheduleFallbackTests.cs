using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ObjeX.Api.Startup;
using ObjeX.Core.Interfaces;
using ObjeX.Core.Models;
using ObjeX.Infrastructure.Data;

namespace ObjeX.Tests.Integration;

/// <summary>A stored row this host cannot apply as it is: the Jobs page shows what runs instead and why, not only the log.</summary>
public class JobScheduleFallbackTests(ObjeXFactory factory) : IClassFixture<ObjeXFactory>
{
    private RecurringJobStatus Job(string id) => factory.Services.GetRequiredService<IJobMonitor>().GetRecurringJobs().Single(j => j.Id == id);

    // Written past the scheduler's checks, as a database moved from another host or edited by hand would hold it.
    private async Task StoreAndRegisterAsync(JobSchedule schedule)
    {
        await using (var db = await factory.Services.GetRequiredService<IDbContextFactory<ObjeXDbContext>>().CreateDbContextAsync())
        {
            db.JobSchedules.Add(schedule);
            await db.SaveChangesAsync();
        }
        BackgroundJobs.RegisterRecurringJobs(factory.Services);
    }

    [Fact]
    public async Task AnUnknownZone_ShowsThatTheCronRunsInUtc()
    {
        await StoreAndRegisterAsync(new JobSchedule { JobId = "verify-blob-integrity", Cron = "15 1 * * 2", TimeZone = "Mars/Olympus_Mons" });

        var job = Job("verify-blob-integrity");

        Assert.Equal("This server does not know the time zone Mars/Olympus_Mons. The job runs its schedule in UTC.", job.Warning);
        Assert.Equal(("15 1 * * 2", "UTC"), (job.Cron, job.TimeZone));
    }

    [Fact]
    public async Task AnInvalidCron_ShowsThatTheDefaultRuns()
    {
        await StoreAndRegisterAsync(new JobSchedule { JobId = "cleanup-orphaned-blobs", Cron = "61 * * * *", TimeZone = "UTC" });

        var job = Job("cleanup-orphaned-blobs");

        Assert.Equal("The stored schedule \"61 * * * *\" is invalid. The job runs on its default schedule.", job.Warning);
        Assert.Equal(("0 3 * * 0", "UTC"), (job.Cron, job.TimeZone));
    }

    [Fact]
    public void AScheduleThatApplies_HasNoWarning()
    {
        var job = Job("cleanup-abandoned-multipart");

        Assert.Null(job.Warning);
    }
}
