using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace ObjeX.Tests.Integration;

/// <summary>
/// AWS SDKs stream uploads as aws-chunked when they send a trailing checksum (the CLI does so over
/// HTTPS). The framing must be stripped on every body-writing endpoint, or the chunk headers end up
/// inside the stored object.
/// </summary>
public class AwsChunkedUploadTests(ObjeXFactory factory) : IClassFixture<ObjeXFactory>
{
    private readonly HttpClient _s3 = factory.CreateS3Client();

    /// <summary>Frames <paramref name="data"/> the way an SDK does: signed chunks or unsigned chunks with a checksum trailer.</summary>
    private static byte[] Frame(byte[] data, int chunkSize, bool signedChunks)
    {
        var sig = signedChunks ? ";chunk-signature=" + new string('0', 64) : "";
        using var ms = new MemoryStream();
        for (var offset = 0; offset < data.Length; offset += chunkSize)
        {
            var len = Math.Min(chunkSize, data.Length - offset);
            ms.Write(Encoding.ASCII.GetBytes($"{len:x}{sig}\r\n"));
            ms.Write(data, offset, len);
            ms.Write("\r\n"u8);
        }
        ms.Write(Encoding.ASCII.GetBytes($"0{sig}\r\n"));
        ms.Write(signedChunks ? "\r\n"u8 : "x-amz-checksum-crc32:AAAAAA==\r\n\r\n"u8);
        return ms.ToArray();
    }

    private async Task<HttpResponseMessage> SendChunkedAsync(HttpMethod method, string path, byte[] data, bool signedChunks, int chunkSize = 7)
    {
        var framed = Frame(data, chunkSize, signedChunks);
        var request = new HttpRequestMessage(method, path) { Content = new ByteArrayContent(framed) };
        request.Content.Headers.ContentEncoding.Add("aws-chunked");
        request.Headers.TryAddWithoutValidation("x-amz-decoded-content-length", data.Length.ToString());
        if (!signedChunks)
            request.Headers.TryAddWithoutValidation("x-amz-trailer", "x-amz-checksum-crc32");
        S3RequestSigner.SignRequest(request, factory.AccessKeyId, factory.SecretAccessKey, framed,
            contentSha256: signedChunks ? "STREAMING-AWS4-HMAC-SHA256-PAYLOAD" : "STREAMING-UNSIGNED-PAYLOAD-TRAILER");
        return await _s3.SendAsync(request);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, byte[]? body = null, string? contentType = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = new ByteArrayContent(body);
            if (contentType is not null) request.Content.Headers.ContentType = new(contentType);
        }
        S3RequestSigner.SignRequest(request, factory.AccessKeyId, factory.SecretAccessKey, body);
        return await _s3.SendAsync(request);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Put_AwsChunked_StoresTheDecodedBytes(bool signedChunks)
    {
        var data = Encoding.UTF8.GetBytes("plain object body, twenty-nine bytes");
        var key = $"chunked/put-{(signedChunks ? "signed" : "trailer")}.txt";

        var put = await SendChunkedAsync(HttpMethod.Put, $"/test-bucket/{key}", data, signedChunks);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var get = await SendAsync(HttpMethod.Get, $"/test-bucket/{key}");
        Assert.Equal(data, await get.Content.ReadAsByteArrayAsync());
        Assert.Equal(data.Length, get.Content.Headers.ContentLength);
    }

    [Fact]
    public async Task UploadPart_AwsChunked_StoresTheDecodedBytes()
    {
        const string key = "chunked/multipart.bin";
        // Every part but the last must be at least 5 MiB; the SDKs frame such parts in 64 KiB chunks.
        var part1 = new byte[5 * 1024 * 1024];
        new Random(42).NextBytes(part1);
        var part2 = Encoding.UTF8.GetBytes("second part sent with an unsigned trailer");

        var init = await SendAsync(HttpMethod.Post, $"/test-bucket/{key}?uploads");
        var uploadId = Regex.Match(await init.Content.ReadAsStringAsync(), "<UploadId>([^<]+)</UploadId>").Groups[1].Value;

        var up1 = await SendChunkedAsync(HttpMethod.Put, $"/test-bucket/{key}?partNumber=1&uploadId={uploadId}", part1, signedChunks: true, chunkSize: 64 * 1024);
        var up2 = await SendChunkedAsync(HttpMethod.Put, $"/test-bucket/{key}?partNumber=2&uploadId={uploadId}", part2, signedChunks: false);
        Assert.True(up1.StatusCode == HttpStatusCode.OK, await up1.Content.ReadAsStringAsync());
        Assert.True(up2.StatusCode == HttpStatusCode.OK, await up2.Content.ReadAsStringAsync());

        var completeXml = Encoding.UTF8.GetBytes(
            "<CompleteMultipartUpload>" +
            $"<Part><PartNumber>1</PartNumber><ETag>{up1.Headers.ETag!.Tag}</ETag></Part>" +
            $"<Part><PartNumber>2</PartNumber><ETag>{up2.Headers.ETag!.Tag}</ETag></Part>" +
            "</CompleteMultipartUpload>");
        var complete = await SendAsync(HttpMethod.Post, $"/test-bucket/{key}?uploadId={uploadId}", completeXml, "application/xml");
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);

        var get = await SendAsync(HttpMethod.Get, $"/test-bucket/{key}");
        Assert.Equal(part1.Concat(part2).ToArray(), await get.Content.ReadAsByteArrayAsync());
        Assert.Equal(part1.Length + part2.Length, get.Content.Headers.ContentLength);
    }
}
