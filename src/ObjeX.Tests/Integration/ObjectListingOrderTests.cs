using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ObjeX.Core.Interfaces;
using ObjeX.Core.Models;
using ObjeX.Infrastructure.Data;

namespace ObjeX.Tests.Integration;

/// <summary>
/// S3 lists keys in UTF-8 byte order, so the listing must not depend on insert order or on a
/// locale collation: "B" comes before "a", and "a.txt" before "a/1.txt".
/// </summary>
public class ObjectListingOrderTests(ObjeXFactory factory) : IClassFixture<ObjeXFactory>
{
    private async Task SeedAsync(string bucket, params string[] keys)
    {
        using var scope = factory.CreateScope();
        var metadata = scope.ServiceProvider.GetRequiredService<IMetadataService>();
        var db = scope.ServiceProvider.GetRequiredService<ObjeXDbContext>();
        var ownerId = await db.Users.Select(u => u.Id).FirstAsync();

        await metadata.CreateBucketAsync(new Bucket { Name = bucket, OwnerId = ownerId });
        foreach (var key in keys)
            await metadata.SaveObjectAsync(new BlobObject { BucketName = bucket, Key = key, ETag = "e", Size = 1 });
    }

    private async Task<ListObjectsResult> ListAsync(string bucket, string? prefix = null, string? delimiter = null)
    {
        using var scope = factory.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IMetadataService>()
            .ListObjectsAsync(bucket, prefix, delimiter);
    }

    [Fact]
    public async Task WithoutDelimiter_KeysComeBackInByteOrder()
    {
        await SeedAsync("order-plain", "b.txt", "a/2.txt", "B.txt", "a/1.txt", "a.txt", "Z.txt", "ä.txt");

        var result = await ListAsync("order-plain");

        Assert.Equal(["B.txt", "Z.txt", "a.txt", "a/1.txt", "a/2.txt", "b.txt", "ä.txt"],
            result.Objects.Select(o => o.Key));
    }

    [Fact]
    public async Task WithDelimiter_ObjectsAndCommonPrefixesAreOrdered()
    {
        await SeedAsync("order-delim", "b.txt", "a/2.txt", "B.txt", "a/1.txt", "a.txt", "Z.txt", "ä.txt");

        var result = await ListAsync("order-delim", delimiter: "/");

        Assert.Equal(["B.txt", "Z.txt", "a.txt", "b.txt", "ä.txt"], result.Objects.Select(o => o.Key));
        Assert.Equal(["a/"], result.CommonPrefixes);
    }

    [Fact]
    public async Task WithPrefixAndDelimiter_KeysBelowThePrefixAreOrdered()
    {
        await SeedAsync("order-prefix", "b.txt", "a/2.txt", "B.txt", "a/1.txt", "a.txt", "Z.txt", "ä.txt");

        var result = await ListAsync("order-prefix", prefix: "a/", delimiter: "/");

        Assert.Equal(["a/1.txt", "a/2.txt"], result.Objects.Select(o => o.Key));
        Assert.Empty(result.CommonPrefixes);
    }

    [Fact]
    public async Task CommonPrefixes_AreOrdinalOrderedAndDeduplicated()
    {
        await SeedAsync("order-folders", "a/1.txt", "B/1.txt", "a/2.txt", "B/2.txt", "Z/1.txt");

        var result = await ListAsync("order-folders", delimiter: "/");

        Assert.Equal(["B/", "Z/", "a/"], result.CommonPrefixes);
    }
}
