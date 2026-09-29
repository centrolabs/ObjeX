using System.Net;
using System.Text;
using System.Xml.Linq;

using Microsoft.Extensions.DependencyInjection;

using ObjeX.Core.Models;
using ObjeX.Infrastructure.Data;

namespace ObjeX.Tests.Integration;

/// <summary>
/// Paging through ListObjects, ListObjectsV2 and ListObjectVersions. The delimiter cases mirror
/// ceph/s3-tests: a common prefix counts as one entry, and a marker equal to it skips the whole prefix.
/// </summary>
public class S3PaginationTests(ObjeXFactory factory) : IClassFixture<ObjeXFactory>
{
    private static readonly XNamespace S3 = "http://s3.amazonaws.com/doc/2006-03-01/";
    private readonly HttpClient _client = factory.CreateS3Client();

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? body = null)
    {
        var bytes = body is null ? null : Encoding.UTF8.GetBytes(body);
        var request = new HttpRequestMessage(method, path);
        if (bytes is not null)
            request.Content = new ByteArrayContent(bytes);
        S3RequestSigner.SignRequest(request, factory.AccessKeyId, factory.SecretAccessKey, bytes);
        return await _client.SendAsync(request);
    }

    private async Task<string> BucketWithAsync(params string[] keys)
    {
        var bucket = $"page-{Guid.NewGuid():N}"[..20];
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Put, $"/{bucket}")).StatusCode);
        foreach (var key in keys)
            Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Put, $"/{bucket}/{string.Join('/', key.Split('/').Select(Uri.EscapeDataString))}", "x")).StatusCode);
        return bucket;
    }

    private async Task<XElement> ListAsync(string bucket, string query)
    {
        var response = await SendAsync(HttpMethod.Get, $"/{bucket}?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return XDocument.Parse(await response.Content.ReadAsStringAsync()).Root!;
    }

    private static string[] Keys(XElement root) =>
        root.Elements().Where(e => e.Name.LocalName is "Contents" or "Version").Select(e => e.Element(S3 + "Key")!.Value).ToArray();

    private static string[] Prefixes(XElement root) =>
        root.Elements(S3 + "CommonPrefixes").Select(e => e.Element(S3 + "Prefix")!.Value).ToArray();

    private static string? Value(XElement root, string name) => root.Element(S3 + name)?.Value;

    [Fact]
    public async Task ListObjectsV2_PagesThroughEveryKeyWithContinuationTokens()
    {
        var bucket = await BucketWithAsync("a", "b", "c", "d", "e");
        var seen = new List<string>();
        var truncated = new List<string?>();
        string? token = null;

        do
        {
            var root = await ListAsync(bucket, "list-type=2&max-keys=2" + (token is null ? "" : $"&continuation-token={Uri.EscapeDataString(token)}"));
            seen.AddRange(Keys(root));
            truncated.Add(Value(root, "IsTruncated"));
            Assert.Equal("2", Value(root, "MaxKeys"));
            token = Value(root, "NextContinuationToken");
        } while (token is not null);

        Assert.Equal(["a", "b", "c", "d", "e"], seen);
        Assert.Equal(["true", "true", "false"], truncated);
    }

    [Fact]
    public async Task ListObjects_CommonPrefixesPageLikeS3()
    {
        var bucket = await BucketWithAsync("asdf", "boo/bar", "boo/baz/xyzzy", "cquux/thud", "cquux/bla");

        var first = await ListAsync(bucket, "delimiter=/&max-keys=1");
        Assert.Equal(["asdf"], Keys(first));
        Assert.Equal(("true", "asdf"), (Value(first, "IsTruncated"), Value(first, "NextMarker")));

        var second = await ListAsync(bucket, "delimiter=/&max-keys=1&marker=asdf");
        Assert.Equal(["boo/"], Prefixes(second));
        Assert.Equal(("true", "boo/"), (Value(second, "IsTruncated"), Value(second, "NextMarker")));

        var third = await ListAsync(bucket, "delimiter=/&max-keys=1&marker=boo/");
        Assert.Equal(["cquux/"], Prefixes(third));
        Assert.Equal(("false", null), (Value(third, "IsTruncated"), Value(third, "NextMarker")));
    }

    [Fact]
    public async Task ListObjectsV2_KeyCountIncludesCommonPrefixes()
    {
        var bucket = await BucketWithAsync("a/1", "a/2", "b");

        var root = await ListAsync(bucket, "list-type=2&delimiter=/");

        Assert.Equal("2", Value(root, "KeyCount"));
    }

    [Fact]
    public async Task ListObjectsV2_StartAfterSkipsEarlierKeys()
    {
        var bucket = await BucketWithAsync("a", "b", "c");

        var root = await ListAsync(bucket, "list-type=2&start-after=a");

        Assert.Equal(["b", "c"], Keys(root));
        Assert.Equal("a", Value(root, "StartAfter"));
    }

    [Fact]
    public async Task MaxKeysZero_ListsNothingAndIsNotTruncated()
    {
        var bucket = await BucketWithAsync("a", "b");

        var root = await ListAsync(bucket, "max-keys=0");

        Assert.Empty(Keys(root));
        Assert.Equal("false", Value(root, "IsTruncated"));
    }

    [Theory]
    [InlineData("max-keys=-1")]
    [InlineData("max-keys=blah")]
    [InlineData("list-type=2&continuation-token=%21%21")]
    [InlineData("encoding-type=base64")]
    public async Task InvalidListingParameter_Returns400(string query)
    {
        var bucket = await BucketWithAsync("a");

        var response = await SendAsync(HttpMethod.Get, $"/{bucket}?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("<Code>InvalidArgument</Code>", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task EncodingTypeUrl_EncodesKeysAndPrefixesButKeepsSlashes()
    {
        var bucket = await BucketWithAsync("foo+1/bar", "foo/bar/xyzzy", "quux ab/thud", "asdf+b");

        var root = await ListAsync(bucket, "list-type=2&delimiter=/&encoding-type=url");

        Assert.Equal(["asdf%2Bb"], Keys(root));
        Assert.Equal(["foo%2B1/", "foo/", "quux%20ab/"], Prefixes(root));
    }

    [Fact]
    public async Task EmptyDelimiter_IsNotEchoed()
    {
        var bucket = await BucketWithAsync("a");

        var root = await ListAsync(bucket, "delimiter=");

        Assert.Null(root.Element(S3 + "Delimiter"));
    }

    [Fact]
    public async Task ListObjectVersions_PagesWithKeyMarker()
    {
        var bucket = await BucketWithAsync("a", "b", "c");

        var first = await ListAsync(bucket, "versions&max-keys=2");
        Assert.Equal(["a", "b"], Keys(first));
        Assert.Equal(("true", "b"), (Value(first, "IsTruncated"), Value(first, "NextKeyMarker")));

        var second = await ListAsync(bucket, "versions&max-keys=2&key-marker=b");
        Assert.Equal(["c"], Keys(second));
        Assert.Equal("false", Value(second, "IsTruncated"));
    }

    [Fact]
    public async Task FolderLargerThanOneBatch_IsOneCommonPrefix()
    {
        var bucket = await BucketWithAsync();
        using (var scope = factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ObjeXDbContext>();
            db.BlobObjects.AddRange(Enumerable.Range(0, 2500).Select(i => new BlobObject
            {
                BucketName = bucket, Key = $"big/{i:D5}", ETag = "e", Size = 1, ContentType = "text/plain"
            }));
            db.BlobObjects.Add(new BlobObject { BucketName = bucket, Key = "z", ETag = "e", Size = 1, ContentType = "text/plain" });
            await db.SaveChangesAsync();
        }

        var all = await ListAsync(bucket, "delimiter=/");
        Assert.Equal(["big/"], Prefixes(all));
        Assert.Equal(["z"], Keys(all));

        var afterFolder = await ListAsync(bucket, "delimiter=/&marker=big/");
        Assert.Equal(["z"], Keys(afterFolder));
        Assert.Empty(Prefixes(afterFolder));
    }
}
