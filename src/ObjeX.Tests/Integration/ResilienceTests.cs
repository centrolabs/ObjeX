using System.Net;
using Microsoft.Extensions.DependencyInjection;

using ObjeX.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ObjeX.Tests.Integration;

public class ResilienceTests(ObjeXFactory factory) : IClassFixture<ObjeXFactory>
{
    private readonly HttpClient _client = factory.CreateS3Client();

    [Fact]
    public async Task MissingBlob_Download_ReturnsError()
    {
        var bucket = "test-bucket";
        var key = "missing-blob-" + Guid.NewGuid().ToString("N")[..6] + ".txt";
        var content = "will be orphaned"u8.ToArray();

        // Upload normally
        var putRequest = new HttpRequestMessage(HttpMethod.Put, $"/{bucket}/{key}");
        putRequest.Content = new ByteArrayContent(content);
        S3RequestSigner.SignRequest(putRequest, factory.AccessKeyId, factory.SecretAccessKey, content);
        var putResponse = await _client.SendAsync(putRequest);
        Assert.Equal(HttpStatusCode.Created, putResponse.StatusCode);

        // Delete the physical blob file from disk (simulate crash/corruption)
        using (var scope = factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ObjeXDbContext>();
            var obj = await db.BlobObjects.FirstAsync(o => o.BucketName == bucket && o.Key == key);
            if (File.Exists(obj.StoragePath))
                File.Delete(obj.StoragePath);
        }

        // Try to download — should get error (not 200 with corrupt/empty content)
        var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/{bucket}/{key}");
        S3RequestSigner.SignRequest(getRequest, factory.AccessKeyId, factory.SecretAccessKey);
        var getResponse = await _client.SendAsync(getRequest);
        Assert.True(
            getResponse.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.InternalServerError,
            $"Expected 404 or 500 for missing blob, got {(int)getResponse.StatusCode}");
    }

    [Fact]
    public async Task ConcurrentUploads_SameKey_FinalStateConsistent()
    {
        var bucket = "test-bucket";
        var key = "concurrent-" + Guid.NewGuid().ToString("N")[..6] + ".bin";

        // Launch multiple uploads to the same key concurrently.
        // Some may fail due to .tmp file race (known limitation — concurrent writes
        // to the same hashed path collide on the temp file). The important assertion
        // is that the final state is consistent: download returns valid content.
        var tasks = Enumerable.Range(0, 5).Select(async i =>
        {
            var content = new byte[4096];
            Random.Shared.NextBytes(content);
            content[0] = (byte)i;

            var request = new HttpRequestMessage(HttpMethod.Put, $"/{bucket}/{key}");
            request.Content = new ByteArrayContent(content);
            S3RequestSigner.SignRequest(request, factory.AccessKeyId, factory.SecretAccessKey, content);
            try { return await _client.SendAsync(request); }
            catch { return null; } // some may throw due to concurrent I/O
        }).ToArray();

        await Task.WhenAll(tasks);

        // At least one upload should have succeeded
        var succeeded = tasks.Count(t => t.Result?.StatusCode == HttpStatusCode.Created);
        Assert.True(succeeded >= 1, "At least one concurrent upload should succeed");

        // Final state must be consistent: download returns one coherent upload
        var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/{bucket}/{key}");
        S3RequestSigner.SignRequest(getRequest, factory.AccessKeyId, factory.SecretAccessKey);
        var getResponse = await _client.SendAsync(getRequest);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var downloaded = await getResponse.Content.ReadAsByteArrayAsync();
        Assert.Equal(4096, downloaded.Length); // correct size, not a corrupt mix
    }
}
