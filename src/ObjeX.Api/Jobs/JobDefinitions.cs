using Hangfire;
using Hangfire.Common;
using ObjeX.Core.Interfaces;
using ObjeX.Core.Models;
using ObjeX.Infrastructure.Jobs;

namespace ObjeX.Api.Jobs;

/// <summary>A recurring job of this version. The default cron is UTC.</summary>
public sealed record JobDefinition(string Id, string Name, string DefaultCron, Job Job, JobParameterDefinition? Parameter = null);

/// <summary>A job's one setting, a nullable column of SystemSettings.</summary>
public sealed record JobParameterDefinition(
    string Label, string Unit, string Help, int Default, int Min, int Max,
    Func<SystemSettings, int?> Get, Action<SystemSettings, int?> Set)
{
    public JobParameter Read(SystemSettings settings) => new(Label, Unit, Help, Get(settings), Default, Min, Max);
}

public static class JobDefinitions
{
    /// <summary>In the order the Jobs page lists them: the order of their default schedule.</summary>
    public static readonly IReadOnlyList<JobDefinition> All =
    [
        new("cleanup-orphaned-blobs", "Orphaned blob cleanup", Cron.Weekly(DayOfWeek.Sunday, 3),
            Job.FromExpression<CleanupOrphanedBlobsJob>(j => j.ExecuteAsync()),
            new("Grace", "minutes", "Blob files newer than this are kept: their upload may still be running.",
                CleanupOrphanedBlobsJob.DefaultGraceMinutes, 15, 7 * 24 * 60,
                s => s.OrphanGraceMinutes, (s, v) => s.OrphanGraceMinutes = v)),
        new("verify-blob-integrity", "Blob integrity check", Cron.Weekly(DayOfWeek.Sunday, 4),
            Job.FromExpression<VerifyBlobIntegrityJob>(j => j.ExecuteAsync())),
        new("cleanup-abandoned-multipart", "Abandoned multipart cleanup", Cron.Weekly(DayOfWeek.Sunday, 5),
            Job.FromExpression<CleanupAbandonedMultipartJob>(j => j.ExecuteAsync()),
            new("Abandoned after", "days", "Multipart uploads not completed within this time are deleted with their parts.",
                CleanupAbandonedMultipartJob.DefaultAbandonedDays, 1, 365,
                s => s.AbandonedMultipartDays, (s, v) => s.AbandonedMultipartDays = v)),
        new("recount-bucket-stats", "Bucket stats recount", Cron.Weekly(DayOfWeek.Sunday, 6),
            Job.FromExpression<RecountBucketStatsJob>(j => j.ExecuteAsync())),
    ];

    public static JobDefinition? Find(string id) => All.FirstOrDefault(d => d.Id == id);

    public static JobDefinition? Of(Type? type) => All.FirstOrDefault(d => d.Job.Type == type);
}
