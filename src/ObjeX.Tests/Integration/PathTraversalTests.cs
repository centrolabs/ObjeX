using System.Net;

namespace ObjeX.Tests.Integration;

public class PathTraversalTests(ObjeXFactory factory) : IClassFixture<ObjeXFactory>
{
    private readonly HttpClient _client = factory.CreateS3Client();

    /// <summary>
    /// Encodes an object key for use in a URL path, preserving slashes but encoding
    /// dots-sequences and other special characters so the URI parser doesn't resolve them.
    /// </summary>
    private static string EncodeKeyForUrl(string key)
    {
        return string.Join("/", key.Split('/').Select(Uri.EscapeDataString));
    }

    [Fact]
    public async Task TraversalKey_StoredSafely_RoundTrips()
    {
        // Use a key that contains ".." but has real content after it — tests that
        // the storage layer uses hashed paths and doesn't allow filesystem escape.
        // Keys with ".." pass ObjectKeyValidator when sanitized result is non-empty.
        var bucket = "test-bucket";
        var key = "subdir/..%2F..%2Fetc%2Fpasswd"; // URL-encoded dots, won't be resolved by URI parser
        var content = "not really /etc/passwd"u8.ToArray();

        var putRequest = new HttpRequestMessage(HttpMethod.Put, $"/{bucket}/{key}");
        putRequest.Content = new ByteArrayContent(content);
        S3RequestSigner.SignRequest(putRequest, factory.AccessKeyId, factory.SecretAccessKey, content);
        var putResponse = await _client.SendAsync(putRequest);
        Assert.Equal(HttpStatusCode.Created, putResponse.StatusCode);

        // Verify no blob files exist outside the base path
        AssertAllBlobsWithinBasePath();

        // Download with the same key
        var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/{bucket}/{key}");
        S3RequestSigner.SignRequest(getRequest, factory.AccessKeyId, factory.SecretAccessKey);
        var getResponse = await _client.SendAsync(getRequest);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var downloaded = await getResponse.Content.ReadAsByteArrayAsync();
        Assert.Equal(content, downloaded);
    }

    [Fact]
    public async Task BackslashTraversal_StoredSafely_RoundTrips()
    {
        var bucket = "test-bucket";
        var key = "..\\..\\windows\\system32\\config";
        var content = "not windows"u8.ToArray();

        var encodedKey = EncodeKeyForUrl(key);
        var putRequest = new HttpRequestMessage(HttpMethod.Put, $"/{bucket}/{encodedKey}");
        putRequest.Content = new ByteArrayContent(content);
        S3RequestSigner.SignRequest(putRequest, factory.AccessKeyId, factory.SecretAccessKey, content);
        var putResponse = await _client.SendAsync(putRequest);
        Assert.Equal(HttpStatusCode.Created, putResponse.StatusCode);

        AssertAllBlobsWithinBasePath();

        var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/{bucket}/{encodedKey}");
        S3RequestSigner.SignRequest(getRequest, factory.AccessKeyId, factory.SecretAccessKey);
        var getResponse = await _client.SendAsync(getRequest);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var downloaded = await getResponse.Content.ReadAsByteArrayAsync();
        Assert.Equal(content, downloaded);
    }

    [Fact]
    public async Task DotDotOnly_RejectedByValidator()
    {
        var bucket = "test-bucket";
        var key = "...."; // sanitizes to empty

        var content = "should fail"u8.ToArray();
        var encodedKey = EncodeKeyForUrl(key);
        var putRequest = new HttpRequestMessage(HttpMethod.Put, $"/{bucket}/{encodedKey}");
        putRequest.Content = new ByteArrayContent(content);
        S3RequestSigner.SignRequest(putRequest, factory.AccessKeyId, factory.SecretAccessKey, content);
        var response = await _client.SendAsync(putRequest);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("InvalidArgument", body);
    }

    [Fact]
    public async Task MultipleTraversalVariants_AllBlobsStayWithinBasePath()
    {
        var bucket = "test-bucket";
        string[] keys =
        [
            "../../secret",
            "folder/../../../etc/shadow",
            "normal/../../traversal",
        ];

        foreach (var key in keys)
        {
            var content = System.Text.Encoding.UTF8.GetBytes($"content for {key}");
            var encodedKey = EncodeKeyForUrl(key);
            var putRequest = new HttpRequestMessage(HttpMethod.Put, $"/{bucket}/{encodedKey}");
            putRequest.Content = new ByteArrayContent(content);
            S3RequestSigner.SignRequest(putRequest, factory.AccessKeyId, factory.SecretAccessKey, content);
            await _client.SendAsync(putRequest);
        }

        AssertAllBlobsWithinBasePath();
    }

    private void AssertAllBlobsWithinBasePath()
    {
        if (!Directory.Exists(factory.BlobBasePath)) return;

        var allBlobs = Directory.GetFiles(factory.BlobBasePath, "*.blob", SearchOption.AllDirectories);
        var resolvedBase = Path.GetFullPath(factory.BlobBasePath);
        foreach (var blob in allBlobs)
        {
            var resolved = Path.GetFullPath(blob);
            Assert.StartsWith(resolvedBase, resolved);
        }
    }
}
