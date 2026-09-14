using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ObjeX.Infrastructure.Data;
using ObjeX.Infrastructure.Jobs;

namespace ObjeX.Tests.Integration;

/// <summary>
/// The orphan cleanup decides what a known blob is from bucket and key, never from the absolute
/// path stored at upload time, and it leaves recent files alone because their row may not exist yet.
/// </summary>
public class OrphanCleanupTests(ObjeXFactory factory) : IClassFixture<ObjeXFactory>
{
    private readonly HttpClient _s3 = factory.CreateS3Client();

    private async Task<byte[]> UploadAsync(string key)
    {
        var body = System.Text.Encoding.UTF8.GetBytes($"blob {key}");
        var put = new HttpRequestMessage(HttpMethod.Put, $"/test-bucket/{key}") { Content = new ByteArrayContent(body) };
        S3RequestSigner.SignRequest(put, factory.AccessKeyId, factory.SecretAccessKey, body);
        Assert.Equal(HttpStatusCode.OK, (await _s3.SendAsync(put)).StatusCode);
        return body;
    }

    private async Task<CleanupResult> RunCleanupAsync()
    {
        using var scope = factory.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<CleanupOrphanedBlobsJob>().ExecuteAsync();
    }

    [Fact]
    public async Task MovedVolume_StoredPathsNoLongerMatch_BlobsAreKept()
    {
        var body = await UploadAsync("orphan/moved.txt");

        // Simulate a data directory that was moved: the rows still carry the old absolute paths.
        using (var scope = factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ObjeXDbContext>();
            await db.BlobObjects.Where(o => o.Key == "orphan/moved.txt")
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.StoragePath, o => "/old/volume/" + o.StoragePath));
        }

        await RunCleanupAsync();

        var get = new HttpRequestMessage(HttpMethod.Get, "/test-bucket/orphan/moved.txt");
        S3RequestSigner.SignRequest(get, factory.AccessKeyId, factory.SecretAccessKey);
        var response = await _s3.SendAsync(get);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(body, await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task FreshFileWithoutRow_IsSkipped_UntilItIsOld()
    {
        await UploadAsync("orphan/anchor.txt");
        var bucketDir = Path.Combine(factory.BlobBasePath, "test-bucket");

        // A blob whose row is not written yet looks exactly like this: a new .blob file with no metadata.
        var inFlight = Path.Combine(bucketDir, "ff", "ff", "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff.blob");
        Directory.CreateDirectory(Path.GetDirectoryName(inFlight)!);
        await File.WriteAllBytesAsync(inFlight, "in flight"u8.ToArray());

        await RunCleanupAsync();
        Assert.True(File.Exists(inFlight), "a blob written during the run must not be deleted");

        File.SetLastWriteTimeUtc(inFlight, DateTime.UtcNow.AddDays(-2));
        await RunCleanupAsync();
        Assert.False(File.Exists(inFlight), "an old blob without a row is an orphan");
    }
}
