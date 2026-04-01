using System.Net;

namespace ObjeX.Tests.Integration;

public class S3CopyObjectTests(ObjeXFactory factory) : IClassFixture<ObjeXFactory>
{
    private readonly HttpClient _client = factory.CreateS3Client();

    [Fact]
    public async Task CopyObject_WithinBucket()
    {
        var bucket = "test-bucket";
        var srcKey = "copy-src-" + Guid.NewGuid().ToString("N")[..6] + ".txt";
        var dstKey = "copy-dst-" + Guid.NewGuid().ToString("N")[..6] + ".txt";
        var content = "original content for copy"u8.ToArray();

        // Upload source
        var putRequest = new HttpRequestMessage(HttpMethod.Put, $"/{bucket}/{srcKey}");
        putRequest.Content = new ByteArrayContent(content);
        S3RequestSigner.SignRequest(putRequest, factory.AccessKeyId, factory.SecretAccessKey, content);
        await _client.SendAsync(putRequest);

        // Copy within same bucket
        var copyRequest = new HttpRequestMessage(HttpMethod.Put, $"/{bucket}/{dstKey}");
        copyRequest.Headers.TryAddWithoutValidation("x-amz-copy-source", $"/{bucket}/{srcKey}");
        S3RequestSigner.SignRequest(copyRequest, factory.AccessKeyId, factory.SecretAccessKey);
        var copyResponse = await _client.SendAsync(copyRequest);
        Assert.Equal(HttpStatusCode.OK, copyResponse.StatusCode);
        var copyXml = await copyResponse.Content.ReadAsStringAsync();
        Assert.Contains("CopyObjectResult", copyXml);

        // Download the copy and verify content matches
        var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/{bucket}/{dstKey}");
        S3RequestSigner.SignRequest(getRequest, factory.AccessKeyId, factory.SecretAccessKey);
        var getResponse = await _client.SendAsync(getRequest);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var downloaded = await getResponse.Content.ReadAsByteArrayAsync();
        Assert.Equal(content, downloaded);
    }

    [Fact]
    public async Task CopyObject_CrossBucket()
    {
        var srcBucket = "test-bucket";
        var dstBucket = "copy-dst-bucketxx";
        var srcKey = "cross-src-" + Guid.NewGuid().ToString("N")[..6] + ".txt";
        var dstKey = "cross-dst-" + Guid.NewGuid().ToString("N")[..6] + ".txt";
        var content = "cross-bucket copy"u8.ToArray();

        // Create destination bucket
        var createBucket = new HttpRequestMessage(HttpMethod.Put, $"/{dstBucket}");
        S3RequestSigner.SignRequest(createBucket, factory.AccessKeyId, factory.SecretAccessKey);
        await _client.SendAsync(createBucket);

        // Upload to source bucket
        var putRequest = new HttpRequestMessage(HttpMethod.Put, $"/{srcBucket}/{srcKey}");
        putRequest.Content = new ByteArrayContent(content);
        S3RequestSigner.SignRequest(putRequest, factory.AccessKeyId, factory.SecretAccessKey, content);
        await _client.SendAsync(putRequest);

        // Copy to different bucket
        var copyRequest = new HttpRequestMessage(HttpMethod.Put, $"/{dstBucket}/{dstKey}");
        copyRequest.Headers.TryAddWithoutValidation("x-amz-copy-source", $"/{srcBucket}/{srcKey}");
        S3RequestSigner.SignRequest(copyRequest, factory.AccessKeyId, factory.SecretAccessKey);
        var copyResponse = await _client.SendAsync(copyRequest);
        Assert.Equal(HttpStatusCode.OK, copyResponse.StatusCode);

        // Download from destination and verify
        var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/{dstBucket}/{dstKey}");
        S3RequestSigner.SignRequest(getRequest, factory.AccessKeyId, factory.SecretAccessKey);
        var getResponse = await _client.SendAsync(getRequest);
        var downloaded = await getResponse.Content.ReadAsByteArrayAsync();
        Assert.Equal(content, downloaded);
    }

    [Fact]
    public async Task CopyObject_NonExistentSource_Returns404()
    {
        var bucket = "test-bucket";
        var copyRequest = new HttpRequestMessage(HttpMethod.Put, $"/{bucket}/copy-target.txt");
        copyRequest.Headers.TryAddWithoutValidation("x-amz-copy-source", $"/{bucket}/does-not-exist.txt");
        S3RequestSigner.SignRequest(copyRequest, factory.AccessKeyId, factory.SecretAccessKey);
        var response = await _client.SendAsync(copyRequest);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
