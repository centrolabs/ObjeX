using Microsoft.Extensions.DependencyInjection;
using ObjeX.Core.Interfaces;

namespace ObjeX.Tests.Integration;

public class JobMonitorScheduleTests(ObjeXFactory factory) : IClassFixture<ObjeXFactory>
{
    private IJobMonitor Monitor => factory.Services.GetRequiredService<IJobMonitor>();

    private IJobScheduler Scheduler => factory.Services.GetRequiredService<IJobScheduler>();

    private RecurringJobStatus Job(string id) => Monitor.GetRecurringJobs().Single(j => j.Id == id);

    [Fact]
    public void JobsWithoutAStoredSchedule_ShowTheirDefaultInUtc()
    {
        var job = Job("cleanup-orphaned-blobs");

        Assert.Equal(("0 3 * * 0", "UTC", true, true), (job.Cron, job.TimeZone, job.Enabled, job.IsDefault));
    }

    [Fact]
    public async Task AStoredSchedule_ShowsItsCronAndTimeZone()
    {
        await Scheduler.SaveAsync(new JobScheduleChange("verify-blob-integrity", true, "0 2 * * *", "Europe/Zurich"), "test");

        var job = Job("verify-blob-integrity");

        Assert.Equal(("0 2 * * *", "Europe/Zurich", true, false), (job.Cron, job.TimeZone, job.Enabled, job.IsDefault));
        Assert.NotNull(job.NextRun);
    }

    [Fact]
    public async Task ADisabledJob_IsListedWithItsCronAndWithoutANextRun()
    {
        await Scheduler.SaveAsync(new JobScheduleChange("cleanup-abandoned-multipart", false, "0 6 * * 0", "UTC"), "test");

        var job = Job("cleanup-abandoned-multipart");

        Assert.Equal(("0 6 * * 0", false), (job.Cron, job.Enabled));
        Assert.Null(job.NextRun);
        Assert.Equal(["cleanup-orphaned-blobs", "verify-blob-integrity", "cleanup-abandoned-multipart"], Monitor.GetRecurringJobs().Select(j => j.Id));
    }

    [Fact]
    public async Task RunNow_OnADisabledJob_QueuesOneRun_AndLeavesItDisabled()
    {
        await Scheduler.SaveAsync(new JobScheduleChange("cleanup-abandoned-multipart", false, "0 6 * * 0", "UTC"), "test");

        Monitor.Trigger("cleanup-abandoned-multipart");

        var job = Job("cleanup-abandoned-multipart");
        Assert.Equal(JobRunState.Queued, job.LastJob?.State);
        Assert.False(job.Enabled);
        Assert.Null(job.NextRun);
    }

    [Fact]
    public async Task TheParameter_ShowsItsStoredValueAndItsDefault()
    {
        Assert.Null(Job("verify-blob-integrity").Parameter);
        Assert.Equal((null, 60, 15, 10080), Parameter(Job("cleanup-orphaned-blobs")));

        await Scheduler.SaveAsync(new JobScheduleChange("cleanup-orphaned-blobs", true, "0 3 * * 0", "UTC", 90), "test");

        var job = Job("cleanup-orphaned-blobs");
        Assert.Equal((90, 60, 15, 10080), Parameter(job));
        Assert.False(job.IsDefault);
        await Scheduler.ResetAsync("cleanup-orphaned-blobs", "test");

        static (int?, int, int, int) Parameter(RecurringJobStatus job) => (job.Parameter!.Value, job.Parameter.Default, job.Parameter.Min, job.Parameter.Max);
    }
}
