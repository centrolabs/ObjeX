using System.Net;
using ObjeX.Core.Utilities;

namespace ObjeX.Tests.Integration;

public class S3AuthBoundaryTests(ObjeXFactory factory) : IClassFixture<ObjeXFactory>
{
    private readonly HttpClient _client = factory.CreateS3Client();

    [Fact]
    public async Task NoCredentials_Returns403()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        // No auth headers, just Host
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("AccessDenied", body);
    }

    [Fact]
    public async Task InvalidAccessKey_Returns403()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        S3RequestSigner.SignRequest(request, "OBXBADKEY9999999999", factory.SecretAccessKey);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("InvalidAccessKeyId", body);
    }

    [Fact]
    public async Task WrongSignature_Returns403()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        // Sign with correct key but wrong secret
        S3RequestSigner.SignRequest(request, factory.AccessKeyId, "WrongSecretKeyThatDoesNotMatchAtAll12345678");
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("SignatureDoesNotMatch", body);
    }

    [Fact]
    public async Task ExpiredTimestamp_Returns403()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        // Sign with timestamp 20 minutes in the past (beyond 15-min window)
        S3RequestSigner.SignRequestWithTimestamp(
            request, factory.AccessKeyId, factory.SecretAccessKey,
            DateTime.UtcNow.AddMinutes(-20));
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("RequestExpired", body);
    }

    [Fact]
    public async Task ValidSignature_Returns200()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        S3RequestSigner.SignRequest(request, factory.AccessKeyId, factory.SecretAccessKey);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("ListAllMyBucketsResult", body);
    }

    [Fact]
    public async Task UnsignedPayload_BodyIsNotHashed_Returns200()
    {
        var content = "unsigned payload body"u8.ToArray();
        var request = new HttpRequestMessage(HttpMethod.Put, "/test-bucket/unsigned-payload.txt")
        {
            Content = new ByteArrayContent(content)
        };
        S3RequestSigner.SignRequest(request, factory.AccessKeyId, factory.SecretAccessKey, content,
            contentSha256: "UNSIGNED-PAYLOAD");

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var get = new HttpRequestMessage(HttpMethod.Get, "/test-bucket/unsigned-payload.txt");
        S3RequestSigner.SignRequest(get, factory.AccessKeyId, factory.SecretAccessKey);
        Assert.Equal(content, await (await _client.SendAsync(get)).Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task PayloadHashMismatch_Returns400()
    {
        var declared = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData("a different body"u8.ToArray())).ToLowerInvariant();

        var request = new HttpRequestMessage(HttpMethod.Put, "/test-bucket/payload-mismatch.txt")
        {
            Content = new ByteArrayContent("the actual body"u8.ToArray())
        };
        S3RequestSigner.SignRequest(request, factory.AccessKeyId, factory.SecretAccessKey,
            contentSha256: declared);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("SignatureDoesNotMatch", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PresignedUrl_Valid_Returns200()
    {
        // Upload an object first
        var bucket = "test-bucket";
        var key = "presigned-test.txt";
        var content = "presigned content"u8.ToArray();
        var putRequest = new HttpRequestMessage(HttpMethod.Put, $"/{bucket}/{key}");
        putRequest.Content = new ByteArrayContent(content);
        S3RequestSigner.SignRequest(putRequest, factory.AccessKeyId, factory.SecretAccessKey, content);
        await _client.SendAsync(putRequest);

        // Generate presigned URL (points to localhost:9000)
        var presignedUrl = PresignedUrlGenerator.Generate(
            "http://localhost:9000", bucket, key,
            factory.AccessKeyId, factory.SecretAccessKey,
            expiresSeconds: 3600);

        // Fetch via presigned URL — no Authorization header needed
        var getRequest = new HttpRequestMessage(HttpMethod.Get, presignedUrl);
        // Don't sign — presigned URL carries auth in query params
        var response = await _client.SendAsync(getRequest);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var downloaded = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(content, downloaded);
    }

    [Fact]
    public async Task PresignedUrl_Expired_Returns403()
    {
        var bucket = "test-bucket";
        var key = "presigned-expired.txt";
        var content = "will expire"u8.ToArray();
        var putRequest = new HttpRequestMessage(HttpMethod.Put, $"/{bucket}/{key}");
        putRequest.Content = new ByteArrayContent(content);
        S3RequestSigner.SignRequest(putRequest, factory.AccessKeyId, factory.SecretAccessKey, content);
        await _client.SendAsync(putRequest);

        // Generate presigned URL with 1s expiry
        var presignedUrl = PresignedUrlGenerator.Generate(
            "http://localhost:9000", bucket, key,
            factory.AccessKeyId, factory.SecretAccessKey,
            expiresSeconds: 1);

        // Wait for expiry
        await Task.Delay(3000);

        var getRequest = new HttpRequestMessage(HttpMethod.Get, presignedUrl);
        var response = await _client.SendAsync(getRequest);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
