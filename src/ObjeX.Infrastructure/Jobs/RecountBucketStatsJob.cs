using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ObjeX.Core.Interfaces;
using ObjeX.Infrastructure.Data;

namespace ObjeX.Infrastructure.Jobs;

public record RecountResult(int BucketsChecked, int BucketsCorrected, double DurationSeconds, DateTime Timestamp);

/// <summary>
/// Writes adjust ObjectCount and TotalSize by deltas; this recounts the buckets whose stored numbers no longer match
/// their objects, for example after a crash between a row change and its stats update or a manual database edit.
/// </summary>
public class RecountBucketStatsJob(
    IDbContextFactory<ObjeXDbContext> dbFactory,
    IMetadataService metadataService,
    ILogger<RecountBucketStatsJob> logger)
{
    public async Task<RecountResult> ExecuteAsync()
    {
        var sw = Stopwatch.StartNew();

        List<string> drifted;
        int checkedCount;
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var stats = await db.Buckets
                .Select(b => new
                {
                    b.Name,
                    b.ObjectCount,
                    b.TotalSize,
                    Count = db.BlobObjects.Count(o => o.BucketName == b.Name),
                    Size = db.BlobObjects.Where(o => o.BucketName == b.Name).Sum(o => (long?)o.Size) ?? 0,
                })
                .ToListAsync();
            checkedCount = stats.Count;
            drifted = stats.Where(s => s.ObjectCount != s.Count || s.TotalSize != s.Size).Select(s => s.Name).ToList();
        }

        foreach (var bucket in drifted)
        {
            logger.LogWarning("Bucket {Bucket} had stats that did not match its objects; recounted", bucket);
            await metadataService.UpdateBucketStatsAsync(bucket);
        }

        return new RecountResult(checkedCount, drifted.Count, sw.Elapsed.TotalSeconds, DateTime.UtcNow);
    }
}
