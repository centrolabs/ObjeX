using System.Net;
using System.Text;

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
