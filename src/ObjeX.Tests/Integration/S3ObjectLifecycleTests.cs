using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace ObjeX.Tests.Integration;

public class S3ObjectLifecycleTests(ObjeXFactory factory) : IClassFixture<ObjeXFactory>
{
    private readonly HttpClient _client = factory.CreateS3Client();

    [Fact]
    public async Task Upload_Download_VerifyETag_Delete_Confirm404()
    {
        var bucket = "test-bucket";
        var key = "lifecycle-test.txt";
        var content = "Hello, ObjeX round-trip test!"u8.ToArray();
        var expectedETag = Convert.ToHexString(MD5.HashData(content)).ToLowerInvariant();

        // Upload
        var putRequest = new HttpRequestMessage(HttpMethod.Put, $"/{bucket}/{key}");
        putRequest.Content = new ByteArrayContent(content);
        putRequest.Content.Headers.ContentType = new("text/plain");
        S3RequestSigner.SignRequest(putRequest, factory.AccessKeyId, factory.SecretAccessKey, content);
        var putResponse = await _client.SendAsync(putRequest);
        Assert.Equal(HttpStatusCode.Created, putResponse.StatusCode);

        var returnedETag = putResponse.Headers.ETag?.Tag.Trim('"');
        Assert.Equal(expectedETag, returnedETag);

        // Download
        var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/{bucket}/{key}");
        S3RequestSigner.SignRequest(getRequest, factory.AccessKeyId, factory.SecretAccessKey);
        var getResponse = await _client.SendAsync(getRequest);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var downloaded = await getResponse.Content.ReadAsByteArrayAsync();
        Assert.Equal(content, downloaded);

        // ETag on GET response
        var getETag = getResponse.Headers.ETag?.Tag.Trim('"');
        Assert.Equal(expectedETag, getETag);

        // Delete
        var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/{bucket}/{key}");
        S3RequestSigner.SignRequest(deleteRequest, factory.AccessKeyId, factory.SecretAccessKey);
        var deleteResponse = await _client.SendAsync(deleteRequest);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        // Confirm 404
        var getAfterDelete = new HttpRequestMessage(HttpMethod.Get, $"/{bucket}/{key}");
        S3RequestSigner.SignRequest(getAfterDelete, factory.AccessKeyId, factory.SecretAccessKey);
        var notFoundResponse = await _client.SendAsync(getAfterDelete);
        Assert.Equal(HttpStatusCode.NotFound, notFoundResponse.StatusCode);
    }

    [Fact]
    public async Task Upload_NoContentType_DefaultsToOctetStream()
    {
        var bucket = "test-bucket";
        var key = "no-content-type.bin";
        var content = new byte[] { 1, 2, 3 };

        var putRequest = new HttpRequestMessage(HttpMethod.Put, $"/{bucket}/{key}");
        putRequest.Content = new ByteArrayContent(content);
        S3RequestSigner.SignRequest(putRequest, factory.AccessKeyId, factory.SecretAccessKey, content);
        var putResponse = await _client.SendAsync(putRequest);
        Assert.Equal(HttpStatusCode.Created, putResponse.StatusCode);

        // HEAD to check content type
        var headRequest = new HttpRequestMessage(HttpMethod.Head, $"/{bucket}/{key}");
        S3RequestSigner.SignRequest(headRequest, factory.AccessKeyId, factory.SecretAccessKey);
        var headResponse = await _client.SendAsync(headRequest);
        Assert.Equal(HttpStatusCode.OK, headResponse.StatusCode);
        Assert.Equal("application/octet-stream", headResponse.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Upload_WithCustomMetadata_ReturnedOnHead()
    {
        var bucket = "test-bucket";
        var key = "metadata-test.txt";
        var content = "metadata content"u8.ToArray();

        var putRequest = new HttpRequestMessage(HttpMethod.Put, $"/{bucket}/{key}");
        putRequest.Content = new ByteArrayContent(content);
        putRequest.Content.Headers.ContentType = new("text/plain");
        putRequest.Headers.TryAddWithoutValidation("x-amz-meta-custom-tag", "my-value");
        S3RequestSigner.SignRequest(putRequest, factory.AccessKeyId, factory.SecretAccessKey, content);
        await _client.SendAsync(putRequest);

        // HEAD should return custom metadata
        var headRequest = new HttpRequestMessage(HttpMethod.Head, $"/{bucket}/{key}");
        S3RequestSigner.SignRequest(headRequest, factory.AccessKeyId, factory.SecretAccessKey);
        var headResponse = await _client.SendAsync(headRequest);
        Assert.Equal(HttpStatusCode.OK, headResponse.StatusCode);
        Assert.True(headResponse.Headers.TryGetValues("x-amz-meta-custom-tag", out var values));
        Assert.Equal("my-value", values.First());
    }

    [Fact]
    public async Task Delete_NonExistent_Returns204()
    {
        var bucket = "test-bucket";
        var key = "does-not-exist-" + Guid.NewGuid().ToString("N");

        var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/{bucket}/{key}");
        S3RequestSigner.SignRequest(deleteRequest, factory.AccessKeyId, factory.SecretAccessKey);
        var response = await _client.SendAsync(deleteRequest);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Upload_BinaryContent_RoundTrips()
    {
        var bucket = "test-bucket";
        var key = "binary-roundtrip.bin";
        var content = new byte[8192];
        Random.Shared.NextBytes(content);

        var putRequest = new HttpRequestMessage(HttpMethod.Put, $"/{bucket}/{key}");
        putRequest.Content = new ByteArrayContent(content);
        S3RequestSigner.SignRequest(putRequest, factory.AccessKeyId, factory.SecretAccessKey, content);
        var putResponse = await _client.SendAsync(putRequest);
        Assert.Equal(HttpStatusCode.Created, putResponse.StatusCode);

        var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/{bucket}/{key}");
        S3RequestSigner.SignRequest(getRequest, factory.AccessKeyId, factory.SecretAccessKey);
        var getResponse = await _client.SendAsync(getRequest);
        var downloaded = await getResponse.Content.ReadAsByteArrayAsync();
        Assert.Equal(content, downloaded);
    }

    [Fact]
    public async Task RangeRequest_ReturnsPartialContent()
    {
        var bucket = "test-bucket";
        var key = "range-test-" + Guid.NewGuid().ToString("N")[..6] + ".txt";
        var content = "Hello, Range Request!"u8.ToArray();

        var putRequest = new HttpRequestMessage(HttpMethod.Put, $"/{bucket}/{key}");
        putRequest.Content = new ByteArrayContent(content);
        S3RequestSigner.SignRequest(putRequest, factory.AccessKeyId, factory.SecretAccessKey, content);
        await _client.SendAsync(putRequest);

        var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/{bucket}/{key}");
        getRequest.Headers.Range = new RangeHeaderValue(0, 4); // first 5 bytes
        S3RequestSigner.SignRequest(getRequest, factory.AccessKeyId, factory.SecretAccessKey);
        var response = await _client.SendAsync(getRequest);

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        var partial = await response.Content.ReadAsStringAsync();
        Assert.Equal("Hello", partial);
    }
}
