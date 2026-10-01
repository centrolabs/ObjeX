using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ObjeX.Core.Interfaces;
using ObjeX.Core.Models;
using ObjeX.Infrastructure.Data;
using ObjeX.Infrastructure.Jobs;

namespace ObjeX.Tests.Integration;

/// <summary>The recount repairs ObjectCount and TotalSize of buckets whose stored numbers drifted from their objects.</summary>
public class RecountBucketStatsTests(ObjeXFactory factory) : IClassFixture<ObjeXFactory>
{
    private async Task<ObjeXDbContext> DbAsync()
        => await factory.Services.GetRequiredService<IDbContextFactory<ObjeXDbContext>>().CreateDbContextAsync();

    private async Task CreateAsync(string bucket, params int[] sizes)
    {
        using var scope = factory.CreateScope();
        var metadata = scope.ServiceProvider.GetRequiredService<IMetadataService>();
        var ownerId = await scope.ServiceProvider.GetRequiredService<ObjeXDbContext>().Users.Select(u => u.Id).FirstAsync();
        await metadata.CreateBucketAsync(new Bucket { Name = bucket, OwnerId = ownerId });
        for (var i = 0; i < sizes.Length; i++)
            await metadata.SaveObjectAsync(new BlobObject { BucketName = bucket, Key = $"k{i}", ETag = "e", Size = sizes[i], StoragePath = "unused" });
    }

    private async Task DriftAsync(string bucket, long count, long size)
    {
        await using var db = await DbAsync();
        await db.Buckets.Where(b => b.Name == bucket)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.ObjectCount, count).SetProperty(b => b.TotalSize, size));
    }

    private async Task<(long, long)> StatsAsync(string bucket)
    {
        await using var db = await DbAsync();
        var b = await db.Buckets.SingleAsync(x => x.Name == bucket);
        return (b.ObjectCount, b.TotalSize);
    }

    private async Task<RecountResult> RunAsync()
    {
        using var scope = factory.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<RecountBucketStatsJob>().ExecuteAsync();
    }

    [Fact]
    public async Task DriftedBuckets_AreRecounted_AndOnlyTheyCountAsCorrected()
    {
        await CreateAsync("recount-drifted", 10, 20);
        await CreateAsync("recount-empty");
        await CreateAsync("recount-fine", 5);
        await DriftAsync("recount-drifted", 99, 1);
        await DriftAsync("recount-empty", 3, 300);

        var result = await RunAsync();

        Assert.Equal((2L, 30L), await StatsAsync("recount-drifted"));
        Assert.Equal((0L, 0L), await StatsAsync("recount-empty"));
        Assert.Equal((1L, 5L), await StatsAsync("recount-fine"));
        Assert.Equal(2, result.BucketsCorrected);
        await using var db = await DbAsync();
        Assert.Equal(await db.Buckets.CountAsync(), result.BucketsChecked);
    }

    [Fact]
    public async Task ASecondRun_FindsNothingToCorrect()
    {
        await RunAsync();

        Assert.Equal(0, (await RunAsync()).BucketsCorrected);
    }
}
