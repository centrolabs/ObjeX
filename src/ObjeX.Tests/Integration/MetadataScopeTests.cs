using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ObjeX.Core.Interfaces;
using ObjeX.Core.Models;
using ObjeX.Infrastructure.Data;

namespace ObjeX.Tests.Integration;

/// <summary>
/// A Blazor circuit keeps one scoped context for all its writes. Overwriting or deleting an object that was saved
/// earlier in the same scope used to attach a second instance with the same key; that threw, and afterwards every
/// save in the circuit failed with "Unexpected entry.EntityState: Detached", bucket creation included.
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
}
