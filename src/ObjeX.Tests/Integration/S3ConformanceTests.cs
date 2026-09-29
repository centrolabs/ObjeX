using System.Net;
using System.Text;

namespace ObjeX.Tests.Integration;

/// <summary>
/// Behaviour the ceph/s3-tests suite expects from any S3 server: unsupported subresources never fall
/// through to object or bucket writes, every key is listable and deletable, and errors carry S3 codes.
/// </summary>
public class S3ConformanceTests(ObjeXFactory factory) : IClassFixture<ObjeXFactory>
{
    private readonly HttpClient _client = factory.CreateS3Client();

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? body = null, Action<HttpRequestMessage>? configure = null)
    {
        var bytes = body is null ? null : Encoding.UTF8.GetBytes(body);
        var request = new HttpRequestMessage(method, path);
        if (bytes is not null)
            request.Content = new ByteArrayContent(bytes);
        configure?.Invoke(request);
        S3RequestSigner.SignRequest(request, factory.AccessKeyId, factory.SecretAccessKey, bytes);
        return await _client.SendAsync(request);
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
}
