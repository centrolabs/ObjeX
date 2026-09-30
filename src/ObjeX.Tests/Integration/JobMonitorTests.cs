using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.Extensions.DependencyInjection;
using ObjeX.Core.Interfaces;
using ObjeX.Infrastructure.Jobs;

namespace ObjeX.Tests.Integration;

/// <summary>
/// The factory runs no Hangfire server, so runs are written straight into their final state. The client has no
/// filters: the global AutomaticRetry would turn a FailedState into a retry.
/// </summary>
public class JobMonitorTests(ObjeXFactory factory) : IClassFixture<ObjeXFactory>
{
    private IJobMonitor Monitor => factory.Services.GetRequiredService<IJobMonitor>();

    private BackgroundJobClient Client => new(factory.Services.GetRequiredService<JobStorage>(), new JobFilterCollection());

    [Fact]
    public void RecurringJobs_AreTheDeclaredOnes_WithNamesAndSchedule()
    {
        var jobs = Monitor.GetRecurringJobs();

        Assert.Equal(["cleanup-orphaned-blobs", "verify-blob-integrity", "cleanup-abandoned-multipart"], jobs.Select(j => j.Id));
        Assert.Equal(["Orphaned blob cleanup", "Blob integrity check", "Abandoned multipart cleanup"], jobs.Select(j => j.Name));
        Assert.Equal(["0 3 * * 0", "0 4 * * 0", "0 5 * * 0"], jobs.Select(j => j.Cron));
        Assert.All(jobs, j => Assert.Equal(DayOfWeek.Sunday, j.NextRun!.Value.DayOfWeek));
    }

    [Fact]
    public void SucceededRun_ShowsResultInWordsAndPerformanceDuration()
    {
        var id = Client.Create(Job.FromExpression<CleanupOrphanedBlobsJob>(j => j.ExecuteAsync()),
            new SucceededState(new CleanupResult(1204, 1, 0.25, DateTime.UtcNow), latency: 40, performanceDuration: 250));

        var run = Assert.Single(Monitor.GetRecentRuns(50), r => r.Id == id);

        Assert.Equal("Orphaned blob cleanup", run.Name);
        Assert.Equal(JobRunState.Succeeded, run.State);
        Assert.Equal(TimeSpan.FromMilliseconds(250), run.Duration);
        Assert.Equal($"{1204:N0} blob files checked, 1 orphan deleted", run.Result);
        Assert.Null(run.Error);
    }

    [Fact]
    public void FailedRun_ShowsTheExceptionMessage()
    {
        var id = Client.Create(Job.FromExpression<VerifyBlobIntegrityJob>(j => j.ExecuteAsync()),
            new FailedState(new InvalidOperationException("disk gone")));

        var run = Assert.Single(Monitor.GetRecentRuns(50), r => r.Id == id);

        Assert.Equal("Blob integrity check", run.Name);
        Assert.Equal(JobRunState.Failed, run.State);
        Assert.Equal("disk gone", run.Error);
        Assert.Null(run.Result);
    }

    [Fact]
    public void Trigger_EnqueuesARunAndRecordsItOnTheRecurringJob()
    {
        Monitor.Trigger("cleanup-abandoned-multipart");

        var job = Monitor.GetRecurringJobs().Single(j => j.Id == "cleanup-abandoned-multipart");
        Assert.NotNull(job.LastRun);
        Assert.NotNull(job.LastJob);
        Assert.Equal(JobRunState.Queued, job.LastJob.State);
        Assert.Equal("Abandoned multipart cleanup", job.LastJob.Name);
    }

    [Fact]
    public void FailedRun_Details_CarryTheExceptionAndTheHistory()
    {
        var id = Client.Create(Job.FromExpression<VerifyBlobIntegrityJob>(j => j.ExecuteAsync()),
            new FailedState(new InvalidOperationException("disk gone")));

        var details = Monitor.GetRun(id);

        Assert.NotNull(details);
        Assert.Equal(JobRunState.Failed, details.Run.State);
        Assert.Equal("VerifyBlobIntegrityJob.ExecuteAsync", details.Method);
        Assert.Equal("System.InvalidOperationException", details.ExceptionType);
        Assert.Contains("disk gone", details.ExceptionDetails);
        Assert.NotNull(details.FinishedAt);
        Assert.Equal("Failed", details.History[0].State);
    }

    [Fact]
    public void SucceededRun_Details_CarryTheRawResult()
    {
        var id = Client.Create(Job.FromExpression<CleanupOrphanedBlobsJob>(j => j.ExecuteAsync()),
            new SucceededState(new CleanupResult(3, 0, 0.1, DateTime.UtcNow), latency: 5, performanceDuration: 20));

        var details = Monitor.GetRun(id);

        Assert.NotNull(details);
        Assert.Contains("\"FilesChecked\":3", details.RawResult);
        Assert.Equal("3 blob files checked, 0 orphans deleted", details.Run.Result);
    }

    [Fact]
    public void UnknownRun_HasNoDetails() => Assert.Null(Monitor.GetRun("999999"));

    [Fact]
    public void Retry_EnqueuesAFailedRunAgain()
    {
        var id = Client.Create(Job.FromExpression<VerifyBlobIntegrityJob>(j => j.ExecuteAsync()),
            new FailedState(new InvalidOperationException("disk gone")));

        Assert.True(Monitor.Retry(id));

        Assert.Equal(JobRunState.Queued, Monitor.GetRun(id)!.Run.State);
    }

    [Fact]
    public void Delete_MovesARunToDeleted()
    {
        var id = Client.Create(Job.FromExpression<VerifyBlobIntegrityJob>(j => j.ExecuteAsync()),
            new FailedState(new InvalidOperationException("disk gone")));

        Assert.True(Monitor.Delete(id));

        var details = Monitor.GetRun(id);
        Assert.NotNull(details);
        Assert.Equal("Deleted", details.History[0].State);
        Assert.DoesNotContain(Monitor.GetRecentRuns(50), r => r.Id == id);
    }

    [Fact]
    public void RetryingRun_IsListed()
    {
        var id = Client.Create(Job.FromExpression<VerifyBlobIntegrityJob>(j => j.ExecuteAsync()),
            new ScheduledState(TimeSpan.FromMinutes(5)) { Reason = "Retry attempt 1 of 10: disk gone" });

        var run = Assert.Single(Monitor.GetRecentRuns(50), r => r.Id == id);

        Assert.Equal(JobRunState.Retrying, run.State);
    }

    [Theory]
    [MemberData(nameof(Results))]
    public void Results_ReadAsWords(object result, string expected)
        => Assert.Equal(expected, Api.Jobs.HangfireJobMonitor.Describe(result));

    public static TheoryData<object, string> Results => new()
    {
        { new CleanupResult(1, 0, 0, DateTime.UtcNow), "1 blob file checked, 0 orphans deleted" },
        { new IntegrityResult(7, 1, 2, 3, 0, DateTime.UtcNow), "7 blobs verified, 1 corrupted, 2 missing, 3 multipart skipped" },
        { new IntegrityResult(3, 0, 0, 0, 0, DateTime.UtcNow), "3 blobs verified, 0 corrupted, 0 missing" },
        { new AbandonedMultipartResult(2, 2, 0, DateTime.UtcNow), "2 abandoned uploads found, 2 deleted" },
    };
}
