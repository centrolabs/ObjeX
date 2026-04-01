using System.Net;

namespace ObjeX.Tests.Integration;

public class S3ListObjectsTests(ObjeXFactory factory) : IClassFixture<ObjeXFactory>
{
    private readonly HttpClient _client = factory.CreateS3Client();

    [Fact]
    public async Task ListObjectsV2_ReturnsUploadedObjects()
    {
        var bucket = "test-bucket";
        var key = "listtest-" + Guid.NewGuid().ToString("N")[..6] + ".txt";
        var content = "list me"u8.ToArray();

        await UploadObject(bucket, key, content);

        var listRequest = new HttpRequestMessage(HttpMethod.Get, $"/{bucket}?list-type=2");
        S3RequestSigner.SignRequest(listRequest, factory.AccessKeyId, factory.SecretAccessKey);
        var response = await _client.SendAsync(listRequest);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var xml = await response.Content.ReadAsStringAsync();
        Assert.Contains("ListBucketResult", xml);
        Assert.Contains(key, xml);
    }

    [Fact]
    public async Task ListObjectsV2_PrefixFiltering()
    {
        var bucket = "test-bucket";
        var prefix = "pfx-" + Guid.NewGuid().ToString("N")[..6] + "/";
        var key1 = prefix + "file-a.txt";
        var key2 = prefix + "file-b.txt";
        var otherKey = "other-" + Guid.NewGuid().ToString("N")[..6] + ".txt";

        await UploadObject(bucket, key1, "a"u8.ToArray());
        await UploadObject(bucket, key2, "b"u8.ToArray());
        await UploadObject(bucket, otherKey, "c"u8.ToArray());

        var listRequest = new HttpRequestMessage(HttpMethod.Get,
            $"/{bucket}?list-type=2&prefix={Uri.EscapeDataString(prefix)}");
        S3RequestSigner.SignRequest(listRequest, factory.AccessKeyId, factory.SecretAccessKey);
        var response = await _client.SendAsync(listRequest);
        var xml = await response.Content.ReadAsStringAsync();

        Assert.Contains(key1, xml);
        Assert.Contains(key2, xml);
        Assert.DoesNotContain(otherKey, xml);
    }

    [Fact]
    public async Task ListObjectsV2_DelimiterGrouping()
    {
        var bucket = "test-bucket";
        var folder = "dlm-" + Guid.NewGuid().ToString("N")[..6] + "/";
        var nested = folder + "sub/file.txt";
        var direct = folder + "direct.txt";

        await UploadObject(bucket, nested, "nested"u8.ToArray());
        await UploadObject(bucket, direct, "direct"u8.ToArray());

        var listRequest = new HttpRequestMessage(HttpMethod.Get,
            $"/{bucket}?list-type=2&prefix={Uri.EscapeDataString(folder)}&delimiter=/");
        S3RequestSigner.SignRequest(listRequest, factory.AccessKeyId, factory.SecretAccessKey);
        var response = await _client.SendAsync(listRequest);
        var xml = await response.Content.ReadAsStringAsync();

        // direct.txt should be listed as an object
        Assert.Contains("direct.txt", xml);
        // sub/ should appear as a CommonPrefix, not as individual objects
        Assert.Contains("CommonPrefixes", xml);
        Assert.Contains(folder + "sub/", xml);
    }

    [Fact]
    public async Task ListObjectsV1_Works()
    {
        var bucket = "test-bucket";
        var listRequest = new HttpRequestMessage(HttpMethod.Get, $"/{bucket}");
        S3RequestSigner.SignRequest(listRequest, factory.AccessKeyId, factory.SecretAccessKey);
        var response = await _client.SendAsync(listRequest);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var xml = await response.Content.ReadAsStringAsync();
        Assert.Contains("ListBucketResult", xml);
    }

    private async Task UploadObject(string bucket, string key, byte[] content)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"/{bucket}/{key}");
        request.Content = new ByteArrayContent(content);
        S3RequestSigner.SignRequest(request, factory.AccessKeyId, factory.SecretAccessKey, content);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
