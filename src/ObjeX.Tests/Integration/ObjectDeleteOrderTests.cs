using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ObjeX.Core.Interfaces;
using ObjeX.Core.Models;
using ObjeX.Infrastructure.Data;
using ObjeX.Infrastructure.Metadata;

namespace ObjeX.Tests.Integration;

/// <summary>
/// A metadata row must never point to a missing blob: delete the row first, the blob second,
/// and treat a failed blob deletion as a leftover for the orphan cleanup job, not as a failed delete.
/// </summary>
public class ObjectDeleteOrderTests(ObjectDeleteOrderTests.Factory factory) : IClassFixture<ObjectDeleteOrderTests.Factory>
{
    public class Factory : ObjeXFactory
    {
        public bool FailMetadataDelete { get; set; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IMetadataService>();
                services.AddScoped<IMetadataService>(sp => new FailingDeleteMetadata(
                    new EfCoreMetadataService(sp.GetRequiredService<ObjeXDbContext>()), () => FailMetadataDelete));
            });
        }
    }

    private readonly HttpClient _s3 = factory.CreateS3Client();

    private async Task PutObjectAsync(string bucket, string key, byte[] body)
    {
        var mkBucket = new HttpRequestMessage(HttpMethod.Put, $"/{bucket}");
        S3RequestSigner.SignRequest(mkBucket, factory.AccessKeyId, factory.SecretAccessKey);
        await _s3.SendAsync(mkBucket);

        var put = new HttpRequestMessage(HttpMethod.Put, $"/{bucket}/{key}") { Content = new ByteArrayContent(body) };
        S3RequestSigner.SignRequest(put, factory.AccessKeyId, factory.SecretAccessKey, body);
        var response = await _s3.SendAsync(put);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, byte[]? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = new ByteArrayContent(body);
        S3RequestSigner.SignRequest(request, factory.AccessKeyId, factory.SecretAccessKey, body);
        return await _s3.SendAsync(request);
    }

    [Fact]
    public async Task DeleteObject_WhenMetadataDeleteFails_ObjectStaysRetrievable()
    {
        var content = "still here"u8.ToArray();
        await PutObjectAsync("del-order-meta", "a.txt", content);

        factory.FailMetadataDelete = true;
        try
        {
            var delete = await SendAsync(HttpMethod.Delete, "/del-order-meta/a.txt");
            Assert.NotEqual(HttpStatusCode.NoContent, delete.StatusCode);
        }
        finally
        {
            factory.FailMetadataDelete = false;
        }

        var get = await SendAsync(HttpMethod.Get, "/del-order-meta/a.txt");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(content, await get.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task BatchDelete_WhenMetadataDeleteFails_ObjectsStayRetrievable()
    {
        var content = "batch"u8.ToArray();
        await PutObjectAsync("del-order-batch", "b.txt", content);

        var xml = "<Delete><Object><Key>b.txt</Key></Object></Delete>"u8.ToArray();
        factory.FailMetadataDelete = true;
        try
        {
            var batch = await SendAsync(HttpMethod.Post, "/del-order-batch?delete", xml);
            Assert.Contains("<Error>", await batch.Content.ReadAsStringAsync());
        }
        finally
        {
            factory.FailMetadataDelete = false;
        }

        var get = await SendAsync(HttpMethod.Get, "/del-order-batch/b.txt");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(content, await get.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task DeleteObject_WhenBlobDeleteFails_ObjectIsGoneAndBlobIsLeftForCleanup()
    {
        if (OperatingSystem.IsWindows()) return; // directory write permission is what makes the blob undeletable

        await PutObjectAsync("del-order-blob", "c.txt", "orphan"u8.ToArray());
        var blob = Directory.GetFiles(Path.Combine(factory.BlobBasePath, "del-order-blob"), "*.blob", SearchOption.AllDirectories).Single();
        var dir = Path.GetDirectoryName(blob)!;

        const UnixFileMode readOnly = UnixFileMode.UserRead | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
        const UnixFileMode writable = readOnly | UnixFileMode.UserWrite;

        File.SetUnixFileMode(dir, readOnly);
        try
        {
            var delete = await SendAsync(HttpMethod.Delete, "/del-order-blob/c.txt");
            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

            var get = await SendAsync(HttpMethod.Get, "/del-order-blob/c.txt");
            Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
            Assert.True(File.Exists(blob), "blob should be left behind for the orphan cleanup job");
        }
        finally
        {
            File.SetUnixFileMode(dir, writable);
        }
    }

    private sealed class FailingDeleteMetadata(IMetadataService inner, Func<bool> failDelete) : IMetadataService
    {
        public Task DeleteObjectAsync(string bucketName, string key, string? auditUserId = null, CancellationToken ctk = default)
            => failDelete() ? throw new InvalidOperationException("simulated metadata failure") : inner.DeleteObjectAsync(bucketName, key, auditUserId, ctk);

        public Task<int> DeleteObjectsAsync(string bucketName, IEnumerable<string> keys, string? auditUserId = null, CancellationToken ctk = default)
            => failDelete() ? throw new InvalidOperationException("simulated metadata failure") : inner.DeleteObjectsAsync(bucketName, keys, auditUserId, ctk);

        public Task<Bucket> CreateBucketAsync(Bucket bucket, string? auditUserId = null, CancellationToken ctk = default) => inner.CreateBucketAsync(bucket, auditUserId, ctk);
        public Task<Bucket?> GetBucketAsync(string bucketName, string? ownerFilter = null, CancellationToken ctk = default) => inner.GetBucketAsync(bucketName, ownerFilter, ctk);
        public Task<IEnumerable<Bucket>> ListBucketsAsync(string? ownerFilter = null, CancellationToken ctk = default) => inner.ListBucketsAsync(ownerFilter, ctk);
        public Task DeleteBucketAsync(string bucketName, string userId, bool isPrivileged, string? auditUserId = null, CancellationToken ctk = default) => inner.DeleteBucketAsync(bucketName, userId, isPrivileged, auditUserId, ctk);
        public Task<bool> ExistsBucketAsync(string bucketName, CancellationToken ctk = default) => inner.ExistsBucketAsync(bucketName, ctk);
        public Task<BlobObject> SaveObjectAsync(BlobObject blobObject, string? auditUserId = null, CancellationToken ctk = default) => inner.SaveObjectAsync(blobObject, auditUserId, ctk);
        public Task<BlobObject?> GetObjectAsync(string bucketName, string key, CancellationToken ctk = default) => inner.GetObjectAsync(bucketName, key, ctk);
        public Task<ListObjectsResult> ListObjectsAsync(string bucketName, string? prefix = null, string? delimiter = null, CancellationToken ctk = default) => inner.ListObjectsAsync(bucketName, prefix, delimiter, ctk);
        public Task<IReadOnlyList<BlobObject>> SearchObjectsAsync(string bucketName, string? prefix, string term, int limit, CancellationToken ctk = default) => inner.SearchObjectsAsync(bucketName, prefix, term, limit, ctk);
        public Task<IEnumerable<BlobObject>> ListAllObjectsAsync(CancellationToken ctk = default) => inner.ListAllObjectsAsync(ctk);
        public Task<bool> ExistsObjectAsync(string bucketName, string key, CancellationToken ctk = default) => inner.ExistsObjectAsync(bucketName, key, ctk);
        public Task UpdateBucketStatsAsync(string bucketName, CancellationToken ctk = default) => inner.UpdateBucketStatsAsync(bucketName, ctk);
        public Task<IEnumerable<ContentTypeStats>> GetContentTypeStatsAsync(IEnumerable<string>? bucketNames = null, CancellationToken ctk = default) => inner.GetContentTypeStatsAsync(bucketNames, ctk);
    }
}
