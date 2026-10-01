using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using ObjeX.Core.Interfaces;
using ObjeX.Core.Models;
using ObjeX.Infrastructure.Data;
using ObjeX.Web.Helpers;

namespace ObjeX.Tests.Integration;

/// <summary>PUT /api/upload/{bucket}/{*key}: the browser upload on the UI port, with the same rules as PUT Object on the S3 port.</summary>
public class BrowserUploadTests(ObjeXFactory factory) : IClassFixture<ObjeXFactory>
{
    private const string Bucket = "test-bucket";

    private record UploadResult(string Key, long Size, string ETag);

    private record ErrorResult(string Error);

    private static string Md5Hex(byte[] data) => Convert.ToHexString(MD5.HashData(data)).ToLowerInvariant();

    private static ByteArrayContent Body(byte[] data, string contentType = "application/octet-stream")
    {
        var content = new ByteArrayContent(data);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        return content;
    }

    private Task<UiSession> AdminAsync() => UiSession.LoginAsync(factory, "admin", "admin");

    private async Task<string> CreateUserAsync(string username, string role, long? quota = null)
    {
        using var scope = factory.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = new User { UserName = username, Email = $"{username}@test.local", EmailConfirmed = true, StorageQuotaBytes = quota };
        Assert.True((await userManager.CreateAsync(user, "test1234")).Succeeded);
        await userManager.AddToRoleAsync(user, role);
        return user.Id;
    }

    private async Task CreateBucketAsync(string name, string ownerId)
    {
        using var scope = factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ObjeXDbContext>();
        db.Buckets.Add(new Bucket { Name = name, OwnerId = ownerId });
        await db.SaveChangesAsync();
    }

    private async Task<BlobObject?> GetObjectAsync(string bucket, string key)
    {
        using var scope = factory.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IMetadataService>().GetObjectAsync(bucket, key);
    }

    private async Task<byte[]> ReadBlobAsync(string bucket, string key)
    {
        using var scope = factory.CreateScope();
        await using var stream = await scope.ServiceProvider.GetRequiredService<IObjectStorageService>().RetrieveAsync(bucket, key);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return buffer.ToArray();
    }

    private string[] BucketFiles(string bucket, string pattern = "*")
    {
        var dir = Path.Combine(factory.BlobBasePath, bucket);
        return Directory.Exists(dir) ? Directory.GetFiles(dir, pattern, SearchOption.AllDirectories) : [];
    }

    [Fact]
    public async Task Upload_StoresTheObject_UpdatesTheStats_AndWritesAnAuditEntry()
    {
        var session = await AdminAsync();
        await CreateBucketAsync("upload-stats", session.UserId);
        var data = Encoding.UTF8.GetBytes("hello from the browser");

        var response = await session.PutAsync(FileHelper.UploadUrl("upload-stats", "docs/hello.txt"), Body(data, "text/plain"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<UploadResult>();
        Assert.Equal(new UploadResult("docs/hello.txt", data.Length, Md5Hex(data)), result);

        var obj = await GetObjectAsync("upload-stats", "docs/hello.txt");
        Assert.NotNull(obj);
        Assert.Equal(data.Length, obj.Size);
        Assert.Equal(Md5Hex(data), obj.ETag);
        Assert.Equal("text/plain", obj.ContentType);
        Assert.Equal(data, await ReadBlobAsync("upload-stats", "docs/hello.txt"));

        using var scope = factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ObjeXDbContext>();
        var bucket = await db.Buckets.AsNoTracking().SingleAsync(b => b.Name == "upload-stats");
        Assert.Equal(1, bucket.ObjectCount);
        Assert.Equal(data.Length, bucket.TotalSize);
        Assert.True(await db.AuditEntries.AnyAsync(a =>
            a.Action == "PutObject" && a.UserId == session.UserId && a.BucketName == "upload-stats" && a.Key == "docs/hello.txt"));
    }

    [Fact]
    public async Task WithoutContentType_StoresOctetStream()
    {
        var session = await AdminAsync();

        var response = await session.PutAsync(FileHelper.UploadUrl(Bucket, "untyped.bin"), new ByteArrayContent([1, 2, 3]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/octet-stream", (await GetObjectAsync(Bucket, "untyped.bin"))!.ContentType);
    }

    [Fact]
    public async Task WithoutLogin_Returns401_WithoutRedirect()
    {
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.PutAsync(FileHelper.UploadUrl(Bucket, "anonymous.txt"), Body([1, 2, 3]));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Null(await GetObjectAsync(Bucket, "anonymous.txt"));
    }

    [Fact]
    public async Task WithoutAntiforgeryToken_Returns400_AndWritesNothing()
    {
        var session = await AdminAsync();

        var response = await session.Client.PutAsync(FileHelper.UploadUrl(Bucket, "no-token.txt"), Body([1, 2, 3]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull((await response.Content.ReadFromJsonAsync<ErrorResult>())?.Error);
        Assert.Null(await GetObjectAsync(Bucket, "no-token.txt"));
    }

    [Fact]
    public async Task WithWrongAntiforgeryToken_Returns400_AndWritesNothing()
    {
        var session = await AdminAsync();

        var response = await session.PutAsync(FileHelper.UploadUrl(Bucket, "wrong-token.txt"), Body([1, 2, 3]), token: "not-a-token");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(await GetObjectAsync(Bucket, "wrong-token.txt"));
    }

    [Fact]
    public async Task WithAnotherUsersToken_Returns400()
    {
        var admin = await AdminAsync();
        await CreateUserAsync("upload-token-other", "User");
        var other = await UiSession.LoginAsync(factory, "upload-token-other", "test1234");

        var response = await admin.PutAsync(FileHelper.UploadUrl(Bucket, "other-token.txt"), Body([1, 2, 3]), token: other.Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ForeignBucket_Returns404ForAUser_AndIsAllowedForManagerAndAdmin()
    {
        var ownerId = await CreateUserAsync("upload-owner", "User");
        await CreateBucketAsync("upload-owned", ownerId);
        await CreateUserAsync("upload-stranger", "User");
        await CreateUserAsync("upload-manager", "Manager");

        var stranger = await UiSession.LoginAsync(factory, "upload-stranger", "test1234");
        var refused = await stranger.PutAsync(FileHelper.UploadUrl("upload-owned", "stranger.txt"), Body([1]));
        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
        Assert.Null(await GetObjectAsync("upload-owned", "stranger.txt"));

        var manager = await UiSession.LoginAsync(factory, "upload-manager", "test1234");
        Assert.Equal(HttpStatusCode.OK, (await manager.PutAsync(FileHelper.UploadUrl("upload-owned", "manager.txt"), Body([1]))).StatusCode);

        var admin = await AdminAsync();
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsync(FileHelper.UploadUrl("upload-owned", "admin.txt"), Body([1]))).StatusCode);

        var owner = await UiSession.LoginAsync(factory, "upload-owner", "test1234");
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsync(FileHelper.UploadUrl("upload-owned", "owner.txt"), Body([1]))).StatusCode);
    }

    [Fact]
    public async Task UnknownBucket_Returns404()
    {
        var session = await AdminAsync();

        var response = await session.PutAsync(FileHelper.UploadUrl("no-such-bucket", "a.txt"), Body([1]));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Quota_IsTheBucketOwners_AndAnOverwritePaysOnlyTheGrowth()
    {
        var ownerId = await CreateUserAsync("upload-quota-owner", "User", quota: 1000);
        await CreateBucketAsync("upload-quota", ownerId);
        await CreateUserAsync("upload-quota-manager", "Manager");
        var manager = await UiSession.LoginAsync(factory, "upload-quota-manager", "test1234");
        var url = FileHelper.UploadUrl("upload-quota", "same.bin");

        var first = RandomNumberGenerator.GetBytes(800);
        Assert.Equal(HttpStatusCode.OK, (await manager.PutAsync(url, Body(first))).StatusCode);

        // 800 + 900 exceeds the owner's quota; the growth of 100 over the stored 800 does not.
        var second = RandomNumberGenerator.GetBytes(900);
        Assert.Equal(HttpStatusCode.OK, (await manager.PutAsync(url, Body(second))).StatusCode);

        var tooBig = RandomNumberGenerator.GetBytes(1001);
        var refused = await manager.PutAsync(url, Body(tooBig));
        Assert.Equal((HttpStatusCode)507, refused.StatusCode);
        Assert.NotNull((await refused.Content.ReadFromJsonAsync<ErrorResult>())?.Error);

        var kept = await GetObjectAsync("upload-quota", "same.bin");
        Assert.Equal(900, kept!.Size);
        Assert.Equal(Md5Hex(second), kept.ETag);
        Assert.Equal(second, await ReadBlobAsync("upload-quota", "same.bin"));
        Assert.Empty(BucketFiles("upload-quota", "*.tmp"));
    }

    [Fact]
    public async Task ParallelUploads_NeverPassTheOwnersQuotaTogether()
    {
        var ownerId = await CreateUserAsync("upload-race-owner", "User", quota: 1000);
        await CreateBucketAsync("upload-race", ownerId);
        var owner = await UiSession.LoginAsync(factory, "upload-race-owner", "test1234");

        // Each file fits on its own, any two together do not.
        var responses = await Task.WhenAll(Enumerable.Range(1, 3).Select(i =>
            owner.PutAsync(FileHelper.UploadUrl("upload-race", $"part-{i}.bin"), Body(RandomNumberGenerator.GetBytes(600)))));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(2, responses.Count(r => r.StatusCode == (HttpStatusCode)507));
        using var scope = factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ObjeXDbContext>();
        Assert.Equal(600, (await db.Buckets.AsNoTracking().SingleAsync(b => b.Name == "upload-race")).TotalSize);
        Assert.Empty(BucketFiles("upload-race", "*.tmp"));
    }

    [Fact]
    public async Task FreeDiskBelowTheMinimum_Returns507_AndWritesNothing()
    {
        using var lowDisk = factory.WithWebHostBuilder(b => b.UseSetting("Storage:MinimumFreeDiskBytes", (long.MaxValue / 4).ToString()));
        var session = await UiSession.LoginAsync(lowDisk, "admin", "admin");
        var before = BucketFiles(Bucket).Length;

        var response = await session.PutAsync(FileHelper.UploadUrl(Bucket, "disk-full.txt"), Body([1, 2, 3]));

        Assert.Equal((HttpStatusCode)507, response.StatusCode);
        Assert.NotNull((await response.Content.ReadFromJsonAsync<ErrorResult>())?.Error);
        Assert.Null(await GetObjectAsync(Bucket, "disk-full.txt"));
        Assert.Equal(before, BucketFiles(Bucket).Length);
    }

    public static TheoryData<string> InvalidKeys => ["/leading-slash.txt", "control\u0001char.txt", new string('k', 1025)];

    [Theory]
    [MemberData(nameof(InvalidKeys))]
    public async Task InvalidKey_Returns400_AndWritesNothing(string key)
    {
        var session = await AdminAsync();
        var before = BucketFiles(Bucket).Length;

        var response = await session.PutAsync(FileHelper.UploadUrl(Bucket, key), Body([1, 2, 3]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull((await response.Content.ReadFromJsonAsync<ErrorResult>())?.Error);
        Assert.Null(await GetObjectAsync(Bucket, key));
        Assert.Equal(before, BucketFiles(Bucket).Length);
    }

    public static TheoryData<string> SpecialKeys =>
    [
        "folder/sub folder/with space.txt",
        "Ümlaut/Grüße.txt",
        "decomposed/Grüße.txt",
        "hash/#1 report.pdf",
        "percent/100%.txt",
        "percent/%2F stays literal.txt",
        "plus/a+b=c.txt",
        "mixed/ä #%+ ?&=.bin",
    ];

    [Theory]
    [MemberData(nameof(SpecialKeys))]
    public async Task KeyWithFoldersAndSpecialCharacters_ArrivesByteForByte(string key)
    {
        var session = await AdminAsync();

        var response = await session.PutAsync(FileHelper.UploadUrl(Bucket, key), Body([42]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(key, (await response.Content.ReadFromJsonAsync<UploadResult>())!.Key);
        var stored = await GetObjectAsync(Bucket, key);
        Assert.NotNull(stored);
        Assert.Equal(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(stored.Key));
    }

    [Fact]
    public async Task LargeBody_IsStreamedIntoTheStagedFile_NotBuffered()
    {
        var session = await AdminAsync();
        await CreateBucketAsync("upload-stream", session.UserId);
        const long size = 50L * 1024 * 1024;
        // Halfway through the body the staged file must already hold data; a buffered body is only written once complete.
        var content = new GeneratedContent(size, pauseAt: 20L * 1024 * 1024, pause: () => WaitForStagedDataAsync("upload-stream"));

        var response = await session.PutAsync(FileHelper.UploadUrl("upload-stream", "big.bin"), content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<UploadResult>();
        Assert.Equal(size, result!.Size);
        Assert.Equal(content.Md5Hex, result.ETag);
        Assert.Equal(size, (await GetObjectAsync("upload-stream", "big.bin"))!.Size);
        Assert.Empty(BucketFiles("upload-stream", "*.tmp"));
    }

    [Fact]
    public async Task AbortMidBody_LeavesNoObjectAndNoStagedFile()
    {
        var session = await AdminAsync();
        await CreateBucketAsync("upload-abort", session.UserId);
        // Cancelling the request aborts it on the server like a browser that closes the connection.
        using var abort = new CancellationTokenSource();
        var content = new GeneratedContent(50L * 1024 * 1024, pauseAt: 5L * 1024 * 1024, pause: async () =>
        {
            await WaitForStagedDataAsync("upload-abort");
            abort.Cancel();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            session.PutAsync(FileHelper.UploadUrl("upload-abort", "partial.bin"), content, ctk: abort.Token));

        // The server cleans up after the client has already given up, so wait for it.
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (BucketFiles("upload-abort", "*.tmp").Length > 0 && DateTime.UtcNow < deadline)
            await Task.Delay(50);
        Assert.Empty(BucketFiles("upload-abort", "*.tmp"));
        Assert.Empty(BucketFiles("upload-abort"));
        Assert.Null(await GetObjectAsync("upload-abort", "partial.bin"));
    }

    private async Task WaitForStagedDataAsync(string bucket)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!BucketFiles(bucket, "*.tmp").Any(f => new FileInfo(f).Length > 0))
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("No staged file appeared while the body was still being sent.");
            await Task.Delay(50);
        }
    }

    /// <summary>A body of the given length produced on the fly, so the test never holds it in memory; runs pause once at the given offset.</summary>
    private sealed class GeneratedContent(long length, long pauseAt, Func<Task> pause) : HttpContent
    {
        private readonly IncrementalHash _md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);

        public string Md5Hex { get; private set; } = "";

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            var chunk = new byte[64 * 1024];
            long sent = 0;
            var paused = false;
            while (sent < length)
            {
                if (!paused && sent >= pauseAt)
                {
                    paused = true;
                    await pause();
                }
                var count = (int)Math.Min(chunk.Length, length - sent);
                for (var i = 0; i < count; i++)
                    chunk[i] = (byte)((sent + i) % 251);
                _md5.AppendData(chunk, 0, count);
                await stream.WriteAsync(chunk.AsMemory(0, count));
                sent += count;
            }
            Md5Hex = Convert.ToHexString(_md5.GetHashAndReset()).ToLowerInvariant();
        }

        protected override bool TryComputeLength(out long len)
        {
            len = length;
            return true;
        }
    }
}
