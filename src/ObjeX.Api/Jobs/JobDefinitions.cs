using Hangfire;
using Hangfire.Common;
using ObjeX.Infrastructure.Jobs;

namespace ObjeX.Api.Jobs;

/// <summary>A recurring job of this version. The default cron is UTC.</summary>
public sealed record JobDefinition(string Id, string Name, string DefaultCron, Job Job);

public static class JobDefinitions
{
    /// <summary>In the order the Jobs page lists them: the order of their default schedule.</summary>
    public static readonly IReadOnlyList<JobDefinition> All =
    [
        new("cleanup-orphaned-blobs", "Orphaned blob cleanup", Cron.Weekly(DayOfWeek.Sunday, 3),
            Job.FromExpression<CleanupOrphanedBlobsJob>(j => j.ExecuteAsync())),
        new("verify-blob-integrity", "Blob integrity check", Cron.Weekly(DayOfWeek.Sunday, 4),
            Job.FromExpression<VerifyBlobIntegrityJob>(j => j.ExecuteAsync())),
        new("cleanup-abandoned-multipart", "Abandoned multipart cleanup", Cron.Weekly(DayOfWeek.Sunday, 5),
            Job.FromExpression<CleanupAbandonedMultipartJob>(j => j.ExecuteAsync())),
    ];

    public static JobDefinition? Find(string id) => All.FirstOrDefault(d => d.Id == id);

    public static JobDefinition? Of(Type? type) => All.FirstOrDefault(d => d.Job.Type == type);
}
