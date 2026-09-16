using Microsoft.Extensions.Logging.Abstractions;

using ObjeX.Infrastructure.Hashing;
using ObjeX.Infrastructure.Storage;

namespace ObjeX.Tests.Unit;

public class FileSystemStorageServiceTests : IDisposable
{
    private const string Bucket = "staging-bucket";
    private const string Key = "staged/object.bin";

    private readonly string _basePath = Path.Combine(Path.GetTempPath(), $"objex-staging-{Guid.NewGuid():N}");
    private readonly FileSystemStorageService _storage;

    public FileSystemStorageServiceTests() =>
        _storage = new FileSystemStorageService(_basePath, new Sha256HashService(), NullLogger<FileSystemStorageService>.Instance);

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (Directory.Exists(_basePath))
            Directory.Delete(_basePath, recursive: true);
    }

    private int Count(string pattern) =>
        Directory.EnumerateFiles(_basePath, pattern, SearchOption.AllDirectories).Count();

    [Fact]
    public async Task Stage_WithoutCommit_LeavesNothingBehind()
    {
        await using (await _storage.StageAsync(Bucket, Key, new MemoryStream("staged bytes"u8.ToArray())))
        {
            Assert.Equal(1, Count("*.tmp"));
            Assert.Equal(0, Count("*.blob"));
        }

        Assert.Equal(0, Count("*.tmp"));
        Assert.Equal(0, Count("*.blob"));
        Assert.False(await _storage.ExistsAsync(Bucket, Key));
    }

    [Fact]
    public async Task Stage_ThenCommit_WritesTheBlob()
    {
        var content = "committed bytes"u8.ToArray();

        await using var staged = await _storage.StageAsync(Bucket, Key, new MemoryStream(content));
        Assert.Equal(content.Length, staged.Size);

        var path = await staged.CommitAsync();

        Assert.Equal(0, Count("*.tmp"));
        Assert.Equal(1, Count("*.blob"));
        Assert.True(await _storage.ExistsAsync(Bucket, Key));
        Assert.Equal(content, await File.ReadAllBytesAsync(path));
    }
}
