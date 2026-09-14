using Microsoft.EntityFrameworkCore;
using ObjeX.Infrastructure.Data;

namespace ObjeX.Api.Metrics;

public class BucketMetricsSyncJob(IServiceScopeFactory scopeFactory, ILogger<BucketMetricsSyncJob> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ObjeXDbContext>();
                var buckets = await db.Buckets.AsNoTracking()
                    .Select(b => new { b.Name, b.TotalSize, b.ObjectCount })
                    .ToListAsync(stoppingToken);
                ObjeXMetrics.SyncBuckets(buckets.Select(b => (b.Name, b.TotalSize, (long)b.ObjectCount)));
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Bucket metrics sync failed, next attempt in {Interval}", Interval);
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }
}
