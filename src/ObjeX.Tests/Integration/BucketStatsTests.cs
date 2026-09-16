using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ObjeX.Core.Interfaces;
using ObjeX.Core.Models;
using ObjeX.Infrastructure.Data;

namespace ObjeX.Tests.Integration;

/// <summary>
/// Writes adjust ObjectCount and TotalSize by the delta of the changed object instead of recounting,
/// so every sequence of saves and deletes must leave the same numbers as a full recount.
/// </summary>
public class BucketStatsTests(ObjeXFactory factory) : IClassFixture<ObjeXFactory>
{
    private async Task CreateBucketAsync(string bucket)
    {
        using var scope = factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ObjeXDbContext>();
        var ownerId = await db.Users.Select(u => u.Id).FirstAsync();
        await scope.ServiceProvider.GetRequiredService<IMetadataService>()
            .CreateBucketAsync(new Bucket { Name = bucket, OwnerId = ownerId });
    }

    private async Task PutAsync(string bucket, string key, int size)
    {
        using var scope = factory.CreateScope();
        var storagePath = await scope.ServiceProvider.GetRequiredService<IObjectStorageService>()
            .StoreAsync(bucket, key, new MemoryStream(new byte[size]));
        await scope.ServiceProvider.GetRequiredService<IMetadataService>()
            .SaveObjectAsync(new BlobObject { BucketName = bucket, Key = key, ETag = "e", Size = size, StoragePath = storagePath });
    }

    private async Task DeleteAsync(string bucket, string key)
    {
        using var scope = factory.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IMetadataService>().DeleteObjectAsync(bucket, key);
    }

    private async Task<int> DeleteManyAsync(string bucket, params string[] keys)
    {
        using var scope = factory.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IMetadataService>().DeleteObjectsAsync(bucket, keys);
    }

    private async Task<(long Count, long Size)> StatsAsync(string bucket)
    {
        using var scope = factory.CreateScope();
        var entity = await scope.ServiceProvider.GetRequiredService<IMetadataService>().GetBucketAsync(bucket);
        return (entity!.ObjectCount, entity.TotalSize);
    }

    private async Task<(long Count, long Size)> RecountAsync(string bucket)
    {
        using var scope = factory.CreateScope();
        var metadata = scope.ServiceProvider.GetRequiredService<IMetadataService>();
        await metadata.UpdateBucketStatsAsync(bucket);
        var entity = await metadata.GetBucketAsync(bucket);
        return (entity!.ObjectCount, entity.TotalSize);
    }

    [Fact]
    public async Task SaveOverwriteAndDelete_LeaveTheSameStatsAsARecount()
    {
        const string bucket = "stats-delta";
        await CreateBucketAsync(bucket);

        await PutAsync(bucket, "a.txt", 100);
        await PutAsync(bucket, "b.txt", 50);
        Assert.Equal((2L, 150L), await StatsAsync(bucket));

        await PutAsync(bucket, "a.txt", 300);
        Assert.Equal((2L, 350L), await StatsAsync(bucket));

        await DeleteAsync(bucket, "b.txt");
        Assert.Equal((1L, 300L), await StatsAsync(bucket));
        Assert.Equal(await StatsAsync(bucket), await RecountAsync(bucket));

        Assert.Equal(1, await DeleteManyAsync(bucket, "a.txt"));
        Assert.Equal((0L, 0L), await StatsAsync(bucket));
        Assert.Equal(await StatsAsync(bucket), await RecountAsync(bucket));
    }

    [Fact]
    public async Task BatchDelete_IgnoresUnknownKeys_AndKeepsStatsCorrect()
    {
        const string bucket = "stats-batch";
        await CreateBucketAsync(bucket);

        await PutAsync(bucket, "a.txt", 10);
        await PutAsync(bucket, "b.txt", 20);
        await PutAsync(bucket, "c.txt", 30);

        Assert.Equal(2, await DeleteManyAsync(bucket, "a.txt", "ghost.txt", "b.txt"));

        Assert.Equal((1L, 30L), await StatsAsync(bucket));
        Assert.Equal(await StatsAsync(bucket), await RecountAsync(bucket));
    }

    // 1000 keys is the maximum DeleteObjects accepts, so the key list must still translate to one statement.
    [Fact]
    public async Task BatchDelete_WithAThousandKeys_DeletesThemInOneCall()
    {
        const string bucket = "stats-thousand";
        await CreateBucketAsync(bucket);

        var keys = Enumerable.Range(0, 1000).Select(i => $"k-{i}.txt").ToArray();
        using (var scope = factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ObjeXDbContext>();
            db.BlobObjects.AddRange(keys.Select(k => new BlobObject { BucketName = bucket, Key = k, ETag = "e", Size = 1 }));
            await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<IMetadataService>().UpdateBucketStatsAsync(bucket);
        }
        Assert.Equal((1000L, 1000L), await StatsAsync(bucket));

        Assert.Equal(1000, await DeleteManyAsync(bucket, keys));
        Assert.Equal((0L, 0L), await StatsAsync(bucket));
    }

    [Fact]
    public async Task BatchDelete_WithOnlyUnknownKeys_ChangesNothing()
    {
        const string bucket = "stats-unknown";
        await CreateBucketAsync(bucket);
        await PutAsync(bucket, "a.txt", 40);

        Assert.Equal(0, await DeleteManyAsync(bucket, "ghost-1.txt", "ghost-2.txt"));

        Assert.Equal((1L, 40L), await StatsAsync(bucket));
        Assert.Equal(await StatsAsync(bucket), await RecountAsync(bucket));
    }
}
