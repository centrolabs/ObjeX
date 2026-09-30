using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Hangfire.Storage;
using Microsoft.Extensions.DependencyInjection;
using ObjeX.Api.Startup;
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

        Assert.Equal(["cleanup-abandoned-multipart", "cleanup-orphaned-blobs", "verify-blob-integrity"], ids);
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
}
