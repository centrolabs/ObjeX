using System.Net;
using System.Text;

using Microsoft.Extensions.DependencyInjection;

using ObjeX.Infrastructure.Jobs;

namespace ObjeX.Tests.Integration;

public class S3MultipartTests(ObjeXFactory factory) : IClassFixture<ObjeXFactory>
{
    private readonly HttpClient _client = factory.CreateS3Client();

    [Fact]
    public async Task Multipart_Initiate_UploadParts_Complete_Download()
    {
        var bucket = "test-bucket";
        var key = "multipart-test.bin";

        // Part 1: exactly 5MB (minimum for non-last parts)
        var part1 = new byte[5 * 1024 * 1024];
        Random.Shared.NextBytes(part1);
        // Part 2: small (last part exempt from min size)
        var part2 = "final-part-content"u8.ToArray();

        // 1. Initiate
        var initRequest = new HttpRequestMessage(HttpMethod.Post, $"/{bucket}/{key}?uploads");
        S3RequestSigner.SignRequest(initRequest, factory.AccessKeyId, factory.SecretAccessKey);
        var initResponse = await _client.SendAsync(initRequest);
        Assert.Equal(HttpStatusCode.OK, initResponse.StatusCode);

        var initXml = await initResponse.Content.ReadAsStringAsync();
        Assert.Contains("UploadId", initXml);
        var uploadId = ExtractXmlValue(initXml, "UploadId");
        Assert.False(string.IsNullOrEmpty(uploadId));

        // 2. Upload Part 1
        var put1 = new HttpRequestMessage(HttpMethod.Put, $"/{bucket}/{key}?partNumber=1&uploadId={uploadId}");
        put1.Content = new ByteArrayContent(part1);
        S3RequestSigner.SignRequest(put1, factory.AccessKeyId, factory.SecretAccessKey, part1);
        var put1Response = await _client.SendAsync(put1);
        Assert.Equal(HttpStatusCode.OK, put1Response.StatusCode);
        var etag1 = put1Response.Headers.ETag?.Tag.Trim('"');
        Assert.NotNull(etag1);

        // 3. Upload Part 2
        var put2 = new HttpRequestMessage(HttpMethod.Put, $"/{bucket}/{key}?partNumber=2&uploadId={uploadId}");
        put2.Content = new ByteArrayContent(part2);
        S3RequestSigner.SignRequest(put2, factory.AccessKeyId, factory.SecretAccessKey, part2);
        var put2Response = await _client.SendAsync(put2);
        Assert.Equal(HttpStatusCode.OK, put2Response.StatusCode);
        var etag2 = put2Response.Headers.ETag?.Tag.Trim('"');
        Assert.NotNull(etag2);

        // 4. Complete
        var completeXml = $"""
            <CompleteMultipartUpload>
              <Part><PartNumber>1</PartNumber><ETag>"{etag1}"</ETag></Part>
              <Part><PartNumber>2</PartNumber><ETag>"{etag2}"</ETag></Part>
            </CompleteMultipartUpload>
            """;
        var completeBody = Encoding.UTF8.GetBytes(completeXml);
        var completeRequest = new HttpRequestMessage(HttpMethod.Post, $"/{bucket}/{key}?uploadId={uploadId}");
        completeRequest.Content = new ByteArrayContent(completeBody);
        completeRequest.Content.Headers.ContentType = new("application/xml");
        S3RequestSigner.SignRequest(completeRequest, factory.AccessKeyId, factory.SecretAccessKey, completeBody);
        var completeResponse = await _client.SendAsync(completeRequest);
        Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);

        var completeResponseXml = await completeResponse.Content.ReadAsStringAsync();
        var finalETag = ExtractXmlValue(completeResponseXml, "ETag");
        Assert.NotNull(finalETag);
        Assert.EndsWith("-2", finalETag); // multipart ETag format: hash-partCount

        // 5. Download and verify content = part1 + part2
        var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/{bucket}/{key}");
        S3RequestSigner.SignRequest(getRequest, factory.AccessKeyId, factory.SecretAccessKey);
        var getResponse = await _client.SendAsync(getRequest);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var downloaded = await getResponse.Content.ReadAsByteArrayAsync();
        Assert.Equal(part1.Length + part2.Length, downloaded.Length);
        Assert.Equal(part1, downloaded[..part1.Length]);
        Assert.Equal(part2, downloaded[part1.Length..]);
    }

    [Fact]
    public async Task Multipart_Abort_CleansUp()
    {
        var bucket = "test-bucket";
        var key = "multipart-abort.bin";

        // Initiate
        var initRequest = new HttpRequestMessage(HttpMethod.Post, $"/{bucket}/{key}?uploads");
        S3RequestSigner.SignRequest(initRequest, factory.AccessKeyId, factory.SecretAccessKey);
        var initResponse = await _client.SendAsync(initRequest);
        var uploadId = ExtractXmlValue(await initResponse.Content.ReadAsStringAsync(), "UploadId");

        // Upload a part
        var partData = new byte[1024];
        var putPart = new HttpRequestMessage(HttpMethod.Put, $"/{bucket}/{key}?partNumber=1&uploadId={uploadId}");
        putPart.Content = new ByteArrayContent(partData);
        S3RequestSigner.SignRequest(putPart, factory.AccessKeyId, factory.SecretAccessKey, partData);
        await _client.SendAsync(putPart);

        // Abort
        var abortRequest = new HttpRequestMessage(HttpMethod.Delete, $"/{bucket}/{key}?uploadId={uploadId}");
        S3RequestSigner.SignRequest(abortRequest, factory.AccessKeyId, factory.SecretAccessKey);
        var abortResponse = await _client.SendAsync(abortRequest);
        Assert.Equal(HttpStatusCode.NoContent, abortResponse.StatusCode);

        // Verify upload is gone — trying to complete should fail
        var completeXml = "<CompleteMultipartUpload></CompleteMultipartUpload>";
        var completeBody = Encoding.UTF8.GetBytes(completeXml);
        var completeRequest = new HttpRequestMessage(HttpMethod.Post, $"/{bucket}/{key}?uploadId={uploadId}");
        completeRequest.Content = new ByteArrayContent(completeBody);
        S3RequestSigner.SignRequest(completeRequest, factory.AccessKeyId, factory.SecretAccessKey, completeBody);
        var completeResponse = await _client.SendAsync(completeRequest);
        Assert.Equal(HttpStatusCode.NotFound, completeResponse.StatusCode);
    }

    [Fact]
    public async Task ListMultipartUploads_ReturnsXml()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/test-bucket?uploads");
        S3RequestSigner.SignRequest(request, factory.AccessKeyId, factory.SecretAccessKey);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var xml = await response.Content.ReadAsStringAsync();
        Assert.Contains("ListMultipartUploadsResult", xml);
    }

    [Fact]
    public async Task IntegrityCheck_MultipartObject_IsServedAndSkippedByTheJob()
    {
        var bucket = "test-bucket";
        var key = "multipart-integrity.bin";
        var (finalETag, content) = await UploadTwoPartObjectAsync(bucket, key);
        Assert.EndsWith("-2", finalETag);

        // The stored ETag hashes the part MD5s, so re-hashing the blob must not turn into a 500.
        var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/{bucket}/{key}");
        getRequest.Headers.TryAddWithoutValidation("x-objex-verify-integrity", "true");
        S3RequestSigner.SignRequest(getRequest, factory.AccessKeyId, factory.SecretAccessKey);
        var getResponse = await _client.SendAsync(getRequest);

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.Equal(finalETag, getResponse.Headers.ETag?.Tag.Trim('"'));
        Assert.Equal(content, await getResponse.Content.ReadAsByteArrayAsync());

        using var scope = factory.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<VerifyBlobIntegrityJob>().ExecuteAsync();
        Assert.True(result.Skipped >= 1, "the multipart object must be counted as skipped, not hashed");
        Assert.Equal(0, result.Corrupted);
    }

    /// <summary>Two parts, the first at the 5MB minimum for non-last parts; returns the multipart ETag and the assembled bytes.</summary>
    private async Task<(string ETag, byte[] Content)> UploadTwoPartObjectAsync(string bucket, string key)
    {
        var part1 = new byte[5 * 1024 * 1024];
        Random.Shared.NextBytes(part1);
        var part2 = "second-part"u8.ToArray();

        var initRequest = new HttpRequestMessage(HttpMethod.Post, $"/{bucket}/{key}?uploads");
        S3RequestSigner.SignRequest(initRequest, factory.AccessKeyId, factory.SecretAccessKey);
        var uploadId = ExtractXmlValue(await (await _client.SendAsync(initRequest)).Content.ReadAsStringAsync(), "UploadId");

        var etags = new List<string>();
        foreach (var (partNumber, part) in new[] { (1, part1), (2, part2) })
        {
            var put = new HttpRequestMessage(HttpMethod.Put, $"/{bucket}/{key}?partNumber={partNumber}&uploadId={uploadId}")
            {
                Content = new ByteArrayContent(part)
            };
            S3RequestSigner.SignRequest(put, factory.AccessKeyId, factory.SecretAccessKey, part);
            var response = await _client.SendAsync(put);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            etags.Add(response.Headers.ETag!.Tag.Trim('"'));
        }

        var completeXml = $"""
            <CompleteMultipartUpload>
              <Part><PartNumber>1</PartNumber><ETag>"{etags[0]}"</ETag></Part>
              <Part><PartNumber>2</PartNumber><ETag>"{etags[1]}"</ETag></Part>
            </CompleteMultipartUpload>
            """;
        var completeBody = Encoding.UTF8.GetBytes(completeXml);
        var completeRequest = new HttpRequestMessage(HttpMethod.Post, $"/{bucket}/{key}?uploadId={uploadId}")
        {
            Content = new ByteArrayContent(completeBody)
        };
        completeRequest.Content.Headers.ContentType = new("application/xml");
        S3RequestSigner.SignRequest(completeRequest, factory.AccessKeyId, factory.SecretAccessKey, completeBody);
        var completeResponse = await _client.SendAsync(completeRequest);
        Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);

        var finalETag = ExtractXmlValue(await completeResponse.Content.ReadAsStringAsync(), "ETag");
        return (finalETag, [.. part1, .. part2]);
    }

    private static string ExtractXmlValue(string xml, string tag)
    {
        var start = xml.IndexOf($"<{tag}>", StringComparison.Ordinal);
        if (start < 0) return "";
        start += tag.Length + 2; // skip <Tag>
        var end = xml.IndexOf($"</{tag}>", start, StringComparison.Ordinal);
        if (end < 0) return "";
        return xml[start..end]
            .Replace("&quot;", "")
            .Replace("&amp;", "&")
            .Trim('"');
    }
}
