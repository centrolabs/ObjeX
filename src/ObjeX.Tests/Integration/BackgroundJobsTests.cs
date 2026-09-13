using Hangfire;
using Hangfire.Storage;
using Microsoft.Extensions.DependencyInjection;
using ObjeX.Api.Startup;

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
}
