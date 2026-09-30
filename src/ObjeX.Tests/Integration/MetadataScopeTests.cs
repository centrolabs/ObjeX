using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ObjeX.Core.Interfaces;
using ObjeX.Core.Models;
using ObjeX.Infrastructure.Data;

namespace ObjeX.Tests.Integration;

/// <summary>
/// A Blazor circuit keeps its scoped services for the whole session. When the metadata service shared one context that
/// long, an overwrite or delete of an object saved earlier in the same scope threw ("already being tracked", then
/// "Unexpected entry.EntityState: Detached" on every later save) or wrote stale values after another scope had changed
/// the row. The service now opens a context per call.
/// </summary>
public class MetadataScopeTests(ObjeXFactory factory) : IClassFixture<ObjeXFactory>
{
    [Fact]
    public async Task WritesInOneScope_OverwriteAndDeleteTheirOwnObjects_AndLaterWritesStillWork()
    {
        using var scope = factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ObjeXDbContext>();
        var owner = await db.Users.AsNoTracking().Select(u => u.Id).FirstAsync();
        var metadata = scope.ServiceProvider.GetRequiredService<IMetadataService>();
        BlobObject Object(string key, int size) => new() { BucketName = "scope-src", Key = key, ETag = "e", Size = size, StoragePath = "p" };

        await metadata.CreateBucketAsync(new Bucket { Name = "scope-src", OwnerId = owner }, owner);
        await metadata.SaveObjectAsync(Object("a", 10), owner);
        await metadata.SaveObjectAsync(Object("a", 25), owner);
        await metadata.SaveObjectAsync(Object("b", 7), owner);
        await metadata.DeleteObjectAsync("scope-src", "b", owner);
        await metadata.CreateBucketAsync(new Bucket { Name = "scope-new", OwnerId = owner }, owner);

        var bucket = await db.Buckets.AsNoTracking().SingleAsync(b => b.Name == "scope-src");
        Assert.Equal(1, bucket.ObjectCount);
        Assert.Equal(25, bucket.TotalSize);
        Assert.Equal(25, (await metadata.GetObjectAsync("scope-src", "a"))!.Size);
        Assert.True(await metadata.ExistsBucketAsync("scope-new"));
    }

    [Fact]
    public async Task OverwriteAfterForeignWrite_PersistsNewValues()
    {
        using var circuit = factory.CreateScope();
        var db = circuit.ServiceProvider.GetRequiredService<ObjeXDbContext>();
        var owner = await db.Users.AsNoTracking().Select(u => u.Id).FirstAsync();
        var ui = circuit.ServiceProvider.GetRequiredService<IMetadataService>();
        BlobObject Object(int size, string etag) => new() { BucketName = "stale-b", Key = "a", ETag = etag, Size = size, StoragePath = "p" };

        await ui.CreateBucketAsync(new Bucket { Name = "stale-b", OwnerId = owner }, owner);
        await ui.SaveObjectAsync(Object(10, "e1"), owner);

        using (var s3 = factory.CreateScope())
            await s3.ServiceProvider.GetRequiredService<IMetadataService>().SaveObjectAsync(Object(20, "e2"), owner);

        await ui.SaveObjectAsync(Object(10, "e1"), owner);

        using var check = factory.CreateScope();
        var cdb = check.ServiceProvider.GetRequiredService<ObjeXDbContext>();
        var row = await cdb.BlobObjects.AsNoTracking().SingleAsync(o => o.BucketName == "stale-b" && o.Key == "a");
        var bucket = await cdb.Buckets.AsNoTracking().SingleAsync(b => b.Name == "stale-b");
        Assert.Equal(("e1", 10L, 10L), (row.ETag, row.Size, bucket.TotalSize));
    }

    [Fact]
    public async Task DeleteManyAfterForeignOverwrite_SubtractsTheCurrentSize()
    {
        using var circuit = factory.CreateScope();
        var db = circuit.ServiceProvider.GetRequiredService<ObjeXDbContext>();
        var owner = await db.Users.AsNoTracking().Select(u => u.Id).FirstAsync();
        var ui = circuit.ServiceProvider.GetRequiredService<IMetadataService>();
        BlobObject Object(int size) => new() { BucketName = "drift-b", Key = "a", ETag = "e", Size = size, StoragePath = "p" };

        await ui.CreateBucketAsync(new Bucket { Name = "drift-b", OwnerId = owner }, owner);
        await ui.SaveObjectAsync(Object(10), owner);

        using (var s3 = factory.CreateScope())
            await s3.ServiceProvider.GetRequiredService<IMetadataService>().SaveObjectAsync(Object(30), owner);

        await ui.DeleteObjectsAsync("drift-b", ["a"], owner);

        using var check = factory.CreateScope();
        var bucket = await check.ServiceProvider.GetRequiredService<ObjeXDbContext>().Buckets.AsNoTracking().SingleAsync(b => b.Name == "drift-b");
        Assert.Equal((0L, 0L), (bucket.ObjectCount, bucket.TotalSize));
    }
}
