using System.Security.Cryptography;

using Microsoft.Extensions.DependencyInjection;

using ObjeX.Core.Interfaces;
using ObjeX.Core.Models;
using ObjeX.Infrastructure.Storage;

namespace ObjeX.Tests.Integration;

/// <summary>
/// Before 1.2.5 a key was hashed after stripping ".." and turning "\" into "/", so aliases like
/// "x..y" and "xy" shared one blob. The migration moves each alias blob to the path of its raw key.
/// </summary>
public class LegacyKeyPathMigrationTests(ObjeXFactory factory) : IClassFixture<ObjeXFactory>
{
    private const string Bucket = "test-bucket";

    [Fact]
    public async Task Alias_MovesToItsRawKeyPath_AndSecondRunChangesNothing()
    {
        var key = $"{Guid.NewGuid():N}/a..b\\c";
        var legacyPath = await SeedAsync(key, "alias");

        Assert.Equal(1, await RunAsync());
        Assert.Equal("alias", await ReadAsync(key));
        Assert.False(File.Exists(legacyPath));

        Assert.Equal(0, await RunAsync());
        Assert.Equal("alias", await ReadAsync(key));
    }

    [Fact]
    public async Task Collision_AliasOwnsTheBytes_BlobMovesToTheAlias()
    {
        var id = Guid.NewGuid().ToString("N");
        var owner = $"{id}/xy";
        var alias = $"{id}/x..y";
        await SeedAsync(owner, "owner", writeBlob: false);
        var legacyPath = await SeedAsync(alias, "alias");

        Assert.Equal(1, await RunAsync());

        Assert.Equal("alias", await ReadAsync(alias));
        Assert.False(File.Exists(legacyPath));
    }

    [Fact]
    public async Task Collision_OwnerOwnsTheBytes_BlobStaysWithTheOwner()
    {
        var id = Guid.NewGuid().ToString("N");
        var owner = $"{id}/xy";
        var alias = $"{id}/x..y";
        await SeedAsync(owner, "owner");
        await SeedAsync(alias, "alias", writeBlob: false);

        Assert.Equal(0, await RunAsync());

        Assert.Equal("owner", await ReadAsync(owner));
        using var scope = factory.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<IObjectStorageService>().ExistsAsync(Bucket, alias));
    }

    private async Task<string> SeedAsync(string key, string content, bool writeBlob = true)
    {
        using var scope = factory.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<FileSystemStorageService>();
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        var legacyPath = storage.GetFilePath(Bucket, key.Replace("..", "").Replace('\\', '/'));

        if (writeBlob)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
            await File.WriteAllBytesAsync(legacyPath, bytes);
        }

        await scope.ServiceProvider.GetRequiredService<IMetadataService>().SaveObjectAsync(new BlobObject
        {
            BucketName = Bucket,
            Key = key,
            ETag = Convert.ToHexStringLower(MD5.HashData(bytes)),
            Size = bytes.Length,
            ContentType = "text/plain",
            StoragePath = legacyPath
        });
        return legacyPath;
    }

    private async Task<int> RunAsync()
    {
        using var scope = factory.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<LegacyKeyPathMigration>().RunAsync();
    }

    private async Task<string> ReadAsync(string key)
    {
        using var scope = factory.CreateScope();
        await using var stream = await scope.ServiceProvider.GetRequiredService<IObjectStorageService>().RetrieveAsync(Bucket, key);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }
}
