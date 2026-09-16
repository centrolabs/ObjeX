using System.Net;
using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.DependencyInjection;

using ObjeX.Core.Interfaces;

namespace ObjeX.Tests.Integration;

/// <summary>
/// Content-MD5 is optional, but when a client sends it the payload must match: BadDigest on a
/// mismatch, InvalidDigest when the header is not base64 of 16 bytes, and nothing may be stored.
/// </summary>
public class ContentMd5Tests(ObjeXFactory factory) : IClassFixture<ObjeXFactory>
{
    private const string Bucket = "test-bucket";

    private readonly HttpClient _s3 = factory.CreateS3Client();

    private static string Key(string name) => $"md5/{name}-{Guid.NewGuid():N}.bin";

    private static string Md5Base64(byte[] data) => Convert.ToBase64String(MD5.HashData(data));

    private static string Md5Hex(byte[] data) => Convert.ToHexString(MD5.HashData(data)).ToLowerInvariant();

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string path, byte[]? body = null, string? contentMd5 = null, string? contentType = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = new ByteArrayContent(body);
            if (contentType is not null) request.Content.Headers.ContentType = new(contentType);
            if (contentMd5 is not null) request.Content.Headers.TryAddWithoutValidation("Content-MD5", contentMd5);
        }
        S3RequestSigner.SignRequest(request, factory.AccessKeyId, factory.SecretAccessKey, body);
        return await _s3.SendAsync(request);
    }

    /// <summary>Frames a payload as an aws-chunked body with an unsigned checksum trailer.</summary>
    private static byte[] Frame(byte[] data, int chunkSize)
    {
        using var ms = new MemoryStream();
        for (var offset = 0; offset < data.Length; offset += chunkSize)
        {
            var len = Math.Min(chunkSize, data.Length - offset);
            ms.Write(Encoding.ASCII.GetBytes($"{len:x}\r\n"));
            ms.Write(data, offset, len);
            ms.Write("\r\n"u8);
        }
        ms.Write("0\r\n"u8);
        ms.Write("x-amz-checksum-crc32:AAAAAA==\r\n\r\n"u8);
        return ms.ToArray();
    }

    private static async Task<string> BodyAsync(HttpResponseMessage response) =>
        await response.Content.ReadAsStringAsync();

    [Fact]
    public async Task Put_CorrectContentMd5_StoresObject()
    {
        var key = Key("match");
        var content = "content covered by a correct Content-MD5"u8.ToArray();

        var put = await SendAsync(HttpMethod.Put, $"/{Bucket}/{key}", content, Md5Base64(content), "text/plain");
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.Equal(Md5Hex(content), put.Headers.ETag?.Tag.Trim('"'));

        var head = await SendAsync(HttpMethod.Head, $"/{Bucket}/{key}");
        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
    }

    [Fact]
    public async Task Put_WrongContentMd5_ReturnsBadDigestAndStoresNothing()
    {
        var key = Key("mismatch");
        var content = "the payload that is actually sent"u8.ToArray();

        var put = await SendAsync(HttpMethod.Put, $"/{Bucket}/{key}", content,
            Md5Base64("a completely different payload"u8.ToArray()), "text/plain");

        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
        Assert.Contains("<Code>BadDigest</Code>", await BodyAsync(put));

        var head = await SendAsync(HttpMethod.Head, $"/{Bucket}/{key}");
        Assert.Equal(HttpStatusCode.NotFound, head.StatusCode);

        var storage = factory.Services.GetRequiredService<IObjectStorageService>();
        Assert.False(await storage.ExistsAsync(Bucket, key));
    }

    [Theory]
    [InlineData("not-base64!")]
    [InlineData("AAAAAAAAAAAAAA==")] // base64 of 10 bytes, not 16
    public async Task Put_MalformedContentMd5_ReturnsInvalidDigest(string header)
    {
        var key = Key("malformed");
        var content = "payload"u8.ToArray();

        var put = await SendAsync(HttpMethod.Put, $"/{Bucket}/{key}", content, header, "text/plain");

        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
        Assert.Contains("<Code>InvalidDigest</Code>", await BodyAsync(put));

        var head = await SendAsync(HttpMethod.Head, $"/{Bucket}/{key}");
        Assert.Equal(HttpStatusCode.NotFound, head.StatusCode);
    }

    [Fact]
    public async Task UploadPart_WrongContentMd5_ReturnsBadDigestAndPartIsNotListed()
    {
        var key = Key("part");
        var part = "the part that is actually sent"u8.ToArray();

        var init = await SendAsync(HttpMethod.Post, $"/{Bucket}/{key}?uploads");
        var uploadId = System.Text.RegularExpressions.Regex
            .Match(await BodyAsync(init), "<UploadId>([^<]+)</UploadId>").Groups[1].Value;
        Assert.NotEmpty(uploadId);

        var upload = await SendAsync(HttpMethod.Put, $"/{Bucket}/{key}?partNumber=1&uploadId={uploadId}", part,
            Md5Base64("a completely different part"u8.ToArray()));

        Assert.Equal(HttpStatusCode.BadRequest, upload.StatusCode);
        Assert.Contains("<Code>BadDigest</Code>", await BodyAsync(upload));

        var list = await SendAsync(HttpMethod.Get, $"/{Bucket}/{key}?uploadId={uploadId}");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.DoesNotContain("<PartNumber>", await BodyAsync(list));
    }

    [Fact]
    public async Task DeleteObjects_ContentMd5_IsVerified()
    {
        var key = Key("batch");
        var content = "object targeted by a batch delete"u8.ToArray();
        await SendAsync(HttpMethod.Put, $"/{Bucket}/{key}", content, Md5Base64(content));

        var deleteXml = Encoding.UTF8.GetBytes($"<Delete><Object><Key>{key}</Key></Object></Delete>");

        var wrong = await SendAsync(HttpMethod.Post, $"/{Bucket}?delete", deleteXml,
            Md5Base64("<Delete></Delete>"u8.ToArray()), "application/xml");
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Contains("<Code>BadDigest</Code>", await BodyAsync(wrong));

        var stillThere = await SendAsync(HttpMethod.Head, $"/{Bucket}/{key}");
        Assert.Equal(HttpStatusCode.OK, stillThere.StatusCode);

        var correct = await SendAsync(HttpMethod.Post, $"/{Bucket}?delete", deleteXml, Md5Base64(deleteXml), "application/xml");
        Assert.Equal(HttpStatusCode.OK, correct.StatusCode);
        Assert.Contains(key, await BodyAsync(correct));

        var gone = await SendAsync(HttpMethod.Head, $"/{Bucket}/{key}");
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Fact]
    public async Task Put_AwsChunked_ContentMd5_CoversTheDecodedPayload()
    {
        var key = Key("chunked");
        var content = "an aws-chunked payload split across several chunks"u8.ToArray();
        var framed = Frame(content, chunkSize: 7);

        var request = new HttpRequestMessage(HttpMethod.Put, $"/{Bucket}/{key}") { Content = new ByteArrayContent(framed) };
        request.Content.Headers.ContentEncoding.Add("aws-chunked");
        request.Content.Headers.TryAddWithoutValidation("Content-MD5", Md5Base64(content));
        request.Headers.TryAddWithoutValidation("x-amz-decoded-content-length", content.Length.ToString());
        request.Headers.TryAddWithoutValidation("x-amz-trailer", "x-amz-checksum-crc32");
        S3RequestSigner.SignRequest(request, factory.AccessKeyId, factory.SecretAccessKey, framed,
            contentSha256: "STREAMING-UNSIGNED-PAYLOAD-TRAILER");

        var put = await _s3.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.Equal(Md5Hex(content), put.Headers.ETag?.Tag.Trim('"'));

        var get = await SendAsync(HttpMethod.Get, $"/{Bucket}/{key}");
        Assert.Equal(content, await get.Content.ReadAsByteArrayAsync());
    }
}
