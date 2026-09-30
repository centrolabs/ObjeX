using System.Net;
using System.Text;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

using ObjeX.Core.Models;
using ObjeX.Infrastructure.Data;

namespace ObjeX.Tests.Integration;

/// <summary>
/// Behaviour the ceph/s3-tests suite expects from any S3 server: unsupported subresources never fall
/// through to object or bucket writes, every key is listable and deletable, and errors carry S3 codes.
/// </summary>
public class S3ConformanceTests(ObjeXFactory factory) : IClassFixture<ObjeXFactory>
{
    private readonly HttpClient _client = factory.CreateS3Client();

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? body = null, Action<HttpRequestMessage>? configure = null) =>
        SendAsAsync((factory.AccessKeyId, factory.SecretAccessKey), method, path, body, configure);

    private async Task<HttpResponseMessage> SendAsAsync((string AccessKeyId, string Secret) credential, HttpMethod method, string path, string? body = null, Action<HttpRequestMessage>? configure = null)
    {
        var bytes = body is null ? null : Encoding.UTF8.GetBytes(body);
        var request = new HttpRequestMessage(method, path);
        if (bytes is not null)
            request.Content = new ByteArrayContent(bytes);
        configure?.Invoke(request);
        S3RequestSigner.SignRequest(request, credential.AccessKeyId, credential.Secret, bytes);
        return await _client.SendAsync(request);
    }

    private async Task<(string AccessKeyId, string Secret)> OtherUserAsync()
    {
        using var scope = factory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = new User { UserName = $"other{Guid.NewGuid():N}"[..20], Email = $"{Guid.NewGuid():N}@objex.local" };
        Assert.True((await users.CreateAsync(user, "pass")).Succeeded);
        await users.AddToRoleAsync(user, "User");
        var (credential, secret) = S3Credential.Create("other", user.Id);
        var db = scope.ServiceProvider.GetRequiredService<ObjeXDbContext>();
        db.S3Credentials.Add(credential);
        await db.SaveChangesAsync();
        return (credential.AccessKeyId, secret);
    }

    private async Task<string> NewBucketAsync()
    {
        var bucket = $"conf-{Guid.NewGuid():N}"[..20];
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Put, $"/{bucket}")).StatusCode);
        return bucket;
    }

    [Theory]
    [InlineData("acl")]
    [InlineData("tagging")]
    [InlineData("retention")]
    public async Task PutObjectSubresource_Returns501_AndKeepsTheObject(string subresource)
    {
        var bucket = await NewBucketAsync();
        await SendAsync(HttpMethod.Put, $"/{bucket}/foo", "original");

        var response = await SendAsync(HttpMethod.Put, $"/{bucket}/foo?{subresource}", "<AccessControlPolicy/>");

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
        Assert.Equal("original", await (await SendAsync(HttpMethod.Get, $"/{bucket}/foo")).Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("acl")]
    [InlineData("attributes")]
    [InlineData("tagging")]
    public async Task GetObjectSubresource_Returns501(string subresource)
    {
        var bucket = await NewBucketAsync();
        await SendAsync(HttpMethod.Put, $"/{bucket}/foo", "bar");

        var response = await SendAsync(HttpMethod.Get, $"/{bucket}/foo?{subresource}");

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
        Assert.Contains("<Code>NotImplemented</Code>", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task DeleteObjectSubresource_Returns501_AndKeepsTheObject()
    {
        var bucket = await NewBucketAsync();
        await SendAsync(HttpMethod.Put, $"/{bucket}/foo", "bar");

        Assert.Equal(HttpStatusCode.NotImplemented, (await SendAsync(HttpMethod.Delete, $"/{bucket}/foo?tagging")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Head, $"/{bucket}/foo")).StatusCode);
    }

    [Theory]
    [InlineData("acl")]
    [InlineData("versioning")]
    [InlineData("publicAccessBlock")]
    public async Task PutBucketSubresource_Returns501_NotBucketAlreadyExists(string subresource)
    {
        var bucket = await NewBucketAsync();

        var response = await SendAsync(HttpMethod.Put, $"/{bucket}?{subresource}", "<Configuration/>");

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
    }

    [Fact]
    public async Task DeleteBucketSubresource_Returns501_AndKeepsTheBucket()
    {
        var bucket = await NewBucketAsync();

        Assert.Equal(HttpStatusCode.NotImplemented, (await SendAsync(HttpMethod.Delete, $"/{bucket}?policy")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Head, $"/{bucket}")).StatusCode);
    }

    [Theory]
    [InlineData("?list-type=2")]
    [InlineData("")]
    public async Task KeysEndingInSlash_AreListed(string query)
    {
        var bucket = await NewBucketAsync();
        await SendAsync(HttpMethod.Put, $"/{bucket}/asdf/", "bar");

        var xml = await (await SendAsync(HttpMethod.Get, $"/{bucket}{query}")).Content.ReadAsStringAsync();

        Assert.Contains("<Key>asdf/</Key>", xml);
    }

    [Fact]
    public async Task ListObjectVersions_ListsEachObjectAsItsNullVersion()
    {
        var bucket = await NewBucketAsync();
        await SendAsync(HttpMethod.Put, $"/{bucket}/foo", "bar");

        var response = await SendAsync(HttpMethod.Get, $"/{bucket}?versions");
        var xml = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<ListVersionsResult", xml);
        Assert.Contains("<Key>foo</Key>", xml);
        Assert.Contains("<VersionId>null</VersionId>", xml);
        Assert.Contains("<IsLatest>true</IsLatest>", xml);
    }

    [Fact]
    public async Task GetBucketVersioning_ReportsNeverVersioned()
    {
        var bucket = await NewBucketAsync();

        var response = await SendAsync(HttpMethod.Get, $"/{bucket}?versioning");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<VersioningConfiguration", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task DeleteObjects_DeletesWhitespaceOnlyKey()
    {
        var bucket = await NewBucketAsync();
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Put, $"/{bucket}/%20", "bar")).StatusCode);

        var response = await SendAsync(HttpMethod.Post, $"/{bucket}?delete", "<Delete><Object><Key> </Key></Object></Delete>");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Head, $"/{bucket}/%20")).StatusCode);
    }

    [Fact]
    public async Task FailedPrecondition_CarriesPreconditionFailedCode()
    {
        var bucket = await NewBucketAsync();
        await SendAsync(HttpMethod.Put, $"/{bucket}/foo", "bar");

        var response = await SendAsync(HttpMethod.Get, $"/{bucket}/foo", configure: r => r.Headers.TryAddWithoutValidation("If-Match", "\"ABCORZ\""));

        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
        Assert.Contains("<Code>PreconditionFailed</Code>", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UnsatisfiableRange_CarriesInvalidRangeCode()
    {
        var bucket = await NewBucketAsync();
        await SendAsync(HttpMethod.Put, $"/{bucket}/foo", "bar");

        var response = await SendAsync(HttpMethod.Get, $"/{bucket}/foo", configure: r => r.Headers.TryAddWithoutValidation("Range", "bytes=40-50"));

        Assert.Equal(HttpStatusCode.RequestedRangeNotSatisfiable, response.StatusCode);
        Assert.Contains("<Code>InvalidRange</Code>", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SystemHeaders_AreStoredAndReturned()
    {
        var bucket = await NewBucketAsync();
        await SendAsync(HttpMethod.Put, $"/{bucket}/foo", "bar", r =>
        {
            r.Headers.CacheControl = new() { NoCache = true };
            r.Content!.Headers.ContentDisposition = new("attachment");
            r.Content.Headers.ContentEncoding.Add("gzip");
            r.Content.Headers.ContentLanguage.Add("esperanto");
            r.Content.Headers.Expires = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        });

        foreach (var method in new[] { HttpMethod.Head, HttpMethod.Get })
        {
            var response = await SendAsync(method, $"/{bucket}/foo");
            Assert.Equal("no-cache", response.Headers.CacheControl?.ToString());
            Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.ToString());
            Assert.Equal(["gzip"], response.Content.Headers.ContentEncoding);
            Assert.Equal(["esperanto"], response.Content.Headers.ContentLanguage);
            Assert.Equal(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero), response.Content.Headers.Expires);
        }
    }

    [Fact]
    public async Task AwsChunkedToken_IsNotStoredAsContentEncoding()
    {
        var bucket = await NewBucketAsync();
        await SendAsync(HttpMethod.Put, $"/{bucket}/foo", "bar", r => r.Content!.Headers.ContentEncoding.Add("aws-chunked"));
        await SendAsync(HttpMethod.Put, $"/{bucket}/bar", "bar", r => r.Content!.Headers.TryAddWithoutValidation("Content-Encoding", "gzip, aws-chunked"));

        Assert.Empty((await SendAsync(HttpMethod.Head, $"/{bucket}/foo")).Content.Headers.ContentEncoding);
        Assert.Equal(["gzip"], (await SendAsync(HttpMethod.Head, $"/{bucket}/bar")).Content.Headers.ContentEncoding);
    }

    [Fact]
    public async Task ResponseOverrideParameters_ReplaceTheStoredHeaders()
    {
        var bucket = await NewBucketAsync();
        await SendAsync(HttpMethod.Put, $"/{bucket}/foo", "bar");

        var response = await SendAsync(HttpMethod.Get,
            $"/{bucket}/foo?response-content-type=foo%2Fbar&response-cache-control=no-cache&response-content-disposition=bla&response-content-language=esperanto");

        Assert.Equal("foo/bar", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("no-cache", response.Headers.CacheControl?.ToString());
        Assert.Equal("bla", response.Content.Headers.GetValues("Content-Disposition").Single());
        Assert.Equal(["esperanto"], response.Content.Headers.ContentLanguage);
    }

    private async Task<(string UploadId, string CompleteXml)> UploadOnePartAsync(string bucket, string key, Action<HttpRequestMessage>? initiate = null)
    {
        var init = await SendAsync(HttpMethod.Post, $"/{bucket}/{key}?uploads", configure: initiate);
        var uploadId = System.Xml.Linq.XDocument.Parse(await init.Content.ReadAsStringAsync()).Descendants().Single(e => e.Name.LocalName == "UploadId").Value;
        var part = await SendAsync(HttpMethod.Put, $"/{bucket}/{key}?partNumber=1&uploadId={uploadId}", "part one");
        var etag = part.Headers.ETag!.Tag;
        return (uploadId, $"<CompleteMultipartUpload><Part><PartNumber>1</PartNumber><ETag>{etag}</ETag></Part></CompleteMultipartUpload>");
    }

    [Fact]
    public async Task MultipartUpload_KeepsTheHeadersOfTheInitiateRequest()
    {
        var bucket = await NewBucketAsync();
        var (uploadId, completeXml) = await UploadOnePartAsync(bucket, "mp", r =>
        {
            r.Headers.Add("x-amz-meta-foo", "bar");
            r.Headers.CacheControl = new() { NoCache = true };
            r.Content = new ByteArrayContent([]);
            r.Content.Headers.ContentType = new("text/bla");
        });

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Post, $"/{bucket}/mp?uploadId={uploadId}", completeXml)).StatusCode);

        var head = await SendAsync(HttpMethod.Head, $"/{bucket}/mp");
        Assert.Equal("text/bla", head.Content.Headers.ContentType?.MediaType);
        Assert.Equal("bar", head.Headers.GetValues("x-amz-meta-foo").Single());
        Assert.Equal("no-cache", head.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task CompleteMultipartUpload_Twice_ReturnsTheSameObject()
    {
        var bucket = await NewBucketAsync();
        var (uploadId, completeXml) = await UploadOnePartAsync(bucket, "mp");
        var first = await SendAsync(HttpMethod.Post, $"/{bucket}/mp?uploadId={uploadId}", completeXml);

        var second = await SendAsync(HttpMethod.Post, $"/{bucket}/mp?uploadId={uploadId}", completeXml);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await second.Content.ReadAsStringAsync());
    }

    private async Task<string> PutAsync(string bucket, string key, string body) =>
        (await SendAsync(HttpMethod.Put, $"/{bucket}/{key}", body)).Headers.ETag!.Tag;

    [Fact]
    public async Task PutIfNoneMatchStar_OnExistingKey_FailsAndKeepsTheObject()
    {
        var bucket = await NewBucketAsync();
        await PutAsync(bucket, "foo", "original");

        var response = await SendAsync(HttpMethod.Put, $"/{bucket}/foo", "new", r => r.Headers.TryAddWithoutValidation("If-None-Match", "*"));

        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
        Assert.Equal("original", await (await SendAsync(HttpMethod.Get, $"/{bucket}/foo")).Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Put, $"/{bucket}/new", "x", r => r.Headers.TryAddWithoutValidation("If-None-Match", "*"))).StatusCode);
    }

    [Fact]
    public async Task PutIfMatch_WritesOnlyOverTheMatchingETag()
    {
        var bucket = await NewBucketAsync();
        var etag = await PutAsync(bucket, "foo", "original");

        Assert.Equal(HttpStatusCode.PreconditionFailed, (await SendAsync(HttpMethod.Put, $"/{bucket}/foo", "new", r => r.Headers.TryAddWithoutValidation("If-Match", "\"abc\""))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Put, $"/{bucket}/missing", "new", r => r.Headers.TryAddWithoutValidation("If-Match", etag))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Put, $"/{bucket}/foo", "new", r => r.Headers.TryAddWithoutValidation("If-Match", etag))).StatusCode);
    }

    [Fact]
    public async Task CompleteMultipart_IfNoneMatchStar_FailsOnExistingKey()
    {
        var bucket = await NewBucketAsync();
        await PutAsync(bucket, "mp", "original");
        var (uploadId, completeXml) = await UploadOnePartAsync(bucket, "mp");

        var response = await SendAsync(HttpMethod.Post, $"/{bucket}/mp?uploadId={uploadId}", completeXml, r => r.Headers.TryAddWithoutValidation("If-None-Match", "*"));

        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
        Assert.Equal("original", await (await SendAsync(HttpMethod.Get, $"/{bucket}/mp")).Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("x-amz-copy-source-if-match", "\"abc\"")]
    [InlineData("x-amz-copy-source-if-none-match", null)]
    public async Task CopyWithFailingSourceCondition_Returns412(string header, string? value)
    {
        var bucket = await NewBucketAsync();
        var etag = await PutAsync(bucket, "src", "bar");

        var response = await SendAsync(HttpMethod.Put, $"/{bucket}/dst", configure: r =>
        {
            r.Headers.Add("x-amz-copy-source", $"{bucket}/src");
            r.Headers.TryAddWithoutValidation(header, value ?? etag);
        });

        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Head, $"/{bucket}/dst")).StatusCode);
    }

    private async Task<HttpResponseMessage> UploadPartCopyAsync(string bucket, string uploadId, string source, string? range) =>
        await SendAsync(HttpMethod.Put, $"/{bucket}/dst?partNumber=1&uploadId={uploadId}", configure: r =>
        {
            r.Headers.Add("x-amz-copy-source", $"{bucket}/{source}");
            if (range is not null)
                r.Headers.Add("x-amz-copy-source-range", range);
        });

    private async Task<string> InitiateAsync(string bucket, string key)
    {
        var init = await SendAsync(HttpMethod.Post, $"/{bucket}/{key}?uploads");
        return System.Xml.Linq.XDocument.Parse(await init.Content.ReadAsStringAsync()).Descendants().Single(e => e.Name.LocalName == "UploadId").Value;
    }

    [Theory]
    [InlineData("bytes=2-5", "2345")]
    [InlineData(null, "0123456789")]
    public async Task UploadPartCopy_CopiesTheSourceRangeIntoThePart(string? range, string expected)
    {
        var bucket = await NewBucketAsync();
        await PutAsync(bucket, "src", "0123456789");
        var uploadId = await InitiateAsync(bucket, "dst");

        var copy = await UploadPartCopyAsync(bucket, uploadId, "src", range);
        Assert.Equal(HttpStatusCode.OK, copy.StatusCode);
        var etag = System.Xml.Linq.XDocument.Parse(await copy.Content.ReadAsStringAsync()).Descendants().Single(e => e.Name.LocalName == "ETag").Value;

        var complete = await SendAsync(HttpMethod.Post, $"/{bucket}/dst?uploadId={uploadId}",
            $"<CompleteMultipartUpload><Part><PartNumber>1</PartNumber><ETag>{etag}</ETag></Part></CompleteMultipartUpload>");
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
        Assert.Equal(expected, await (await SendAsync(HttpMethod.Get, $"/{bucket}/dst")).Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("bytes=0-21", "InvalidRange")]
    [InlineData("bytes=5-2", "InvalidRange")]
    [InlineData("0-2", "InvalidArgument")]
    [InlineData("bytes=hello-", "InvalidArgument")]
    [InlineData("bytes=0-2,3-5", "InvalidArgument")]
    public async Task UploadPartCopy_RejectsBadRanges(string range, string code)
    {
        var bucket = await NewBucketAsync();
        await PutAsync(bucket, "src", "0123456789");
        var uploadId = await InitiateAsync(bucket, "dst");

        var response = await UploadPartCopyAsync(bucket, uploadId, "src", range);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains($"<Code>{code}</Code>", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ErrorResponse_CarriesTheRequestIdOfItsHeader()
    {
        var bucket = await NewBucketAsync();

        var response = await SendAsync(HttpMethod.Get, $"/{bucket}/missing");

        var requestId = Assert.Single(response.Headers.GetValues("x-amz-request-id"));
        Assert.False(string.IsNullOrEmpty(requestId));
        Assert.Contains($"<RequestId>{requestId}</RequestId>", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GetObjectInMissingBucket_ReturnsNoSuchBucket()
    {
        var response = await SendAsync(HttpMethod.Get, $"/nope-{Guid.NewGuid():N}"[..20] + "/foo");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("<Code>NoSuchBucket</Code>", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task BucketNameWithPeriods_IsAccepted()
    {
        var bucket = $"dots.{Guid.NewGuid():N}"[..20];

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Put, $"/{bucket}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Put, $"/{bucket}/foo", "bar")).StatusCode);
    }

    [Fact]
    public async Task ListBuckets_PagesWithMaxBuckets()
    {
        var prefix = $"lb{Guid.NewGuid():N}"[..12];
        foreach (var name in new[] { $"{prefix}-a", $"{prefix}-b" })
            await SendAsync(HttpMethod.Put, $"/{name}");

        var first = System.Xml.Linq.XElement.Parse(await (await SendAsync(HttpMethod.Get, $"/?prefix={prefix}&max-buckets=1")).Content.ReadAsStringAsync());
        var token = first.Descendants().SingleOrDefault(e => e.Name.LocalName == "ContinuationToken")?.Value;
        var second = System.Xml.Linq.XElement.Parse(await (await SendAsync(HttpMethod.Get, $"/?prefix={prefix}&max-buckets=1&continuation-token={Uri.EscapeDataString(token!)}")).Content.ReadAsStringAsync());

        static string[] Names(System.Xml.Linq.XElement root) => root.Descendants().Where(e => e.Name.LocalName == "Name").Select(e => e.Value).ToArray();
        Assert.Equal([$"{prefix}-a"], Names(first));
        Assert.Equal([$"{prefix}-b"], Names(second));
        Assert.DoesNotContain(second.Descendants(), e => e.Name.LocalName == "ContinuationToken");
    }

    [Fact]
    public async Task CreateBucket_OwnBucketAgain_IsANoOp_SomeoneElsesIsAConflict()
    {
        var bucket = await NewBucketAsync();
        await PutAsync(bucket, "foo", "bar");

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Put, $"/{bucket}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Head, $"/{bucket}/foo")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(HttpMethod.Put, $"/{bucket}", configure: r => r.Headers.Add("x-amz-acl", "public-read"))).StatusCode);

        var other = await SendAsAsync(await OtherUserAsync(), HttpMethod.Put, $"/{bucket}");
        Assert.Equal(HttpStatusCode.Conflict, other.StatusCode);
        Assert.Contains("<Code>BucketAlreadyExists</Code>", await other.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task KeyWithLiteralPercent_SignsAndRoundTrips()
    {
        var bucket = await NewBucketAsync();

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Put, $"/{bucket}/file%2525.txt", "bar")).StatusCode);

        var xml = await (await SendAsync(HttpMethod.Get, $"/{bucket}")).Content.ReadAsStringAsync();
        Assert.Contains("<Key>file%25.txt</Key>", xml);
    }

    [Fact]
    public async Task CopyOntoItself_WithoutReplace_IsRejected()
    {
        var bucket = await NewBucketAsync();
        await SendAsync(HttpMethod.Put, $"/{bucket}/foo", "bar");

        var response = await SendAsync(HttpMethod.Put, $"/{bucket}/foo", configure: r => r.Headers.Add("x-amz-copy-source", $"{bucket}/foo"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("<Code>InvalidRequest</Code>", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CopyOntoItself_WithReplace_StoresTheNewMetadata()
    {
        var bucket = await NewBucketAsync();
        await SendAsync(HttpMethod.Put, $"/{bucket}/foo", "bar");

        var response = await SendAsync(HttpMethod.Put, $"/{bucket}/foo", configure: r =>
        {
            r.Headers.Add("x-amz-copy-source", $"{bucket}/foo");
            r.Headers.Add("x-amz-metadata-directive", "REPLACE");
            r.Headers.Add("x-amz-meta-foo", "bar");
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var head = await SendAsync(HttpMethod.Head, $"/{bucket}/foo");
        Assert.Equal("bar", head.Headers.GetValues("x-amz-meta-foo").Single());
        Assert.Equal("bar", await (await SendAsync(HttpMethod.Get, $"/{bucket}/foo")).Content.ReadAsStringAsync());
    }
}
