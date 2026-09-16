using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace ObjeX.Tests.Integration;

/// <summary>
/// A write is staged next to the target and only moved into place once every check passed, so a
/// rejected overwrite leaves the previously stored bytes untouched.
/// </summary>
public class StagedWriteTests(ObjeXFactory factory) : IClassFixture<ObjeXFactory>
{
    private const string Bucket = "test-bucket";

    private readonly HttpClient _s3 = factory.CreateS3Client();

    private static string Key(string name) => $"staged/{name}-{Guid.NewGuid():N}.bin";

    private static string Md5Base64(byte[] data) => Convert.ToBase64String(MD5.HashData(data));

    private static string Md5Hex(byte[] data) => Convert.ToHexString(MD5.HashData(data)).ToLowerInvariant();

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string path, byte[]? body = null, string? contentMd5 = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = new ByteArrayContent(body);
            if (contentMd5 is not null) request.Content.Headers.TryAddWithoutValidation("Content-MD5", contentMd5);
        }
        S3RequestSigner.SignRequest(request, factory.AccessKeyId, factory.SecretAccessKey, body);
        return await _s3.SendAsync(request);
    }

    [Fact]
    public async Task Put_WrongContentMd5_LeavesThePreviousObjectIntact()
    {
        var key = Key("overwrite");
        var first = "the body that was stored first"u8.ToArray();

        var stored = await SendAsync(HttpMethod.Put, $"/{Bucket}/{key}", first, Md5Base64(first));
        Assert.Equal(HttpStatusCode.OK, stored.StatusCode);

        var rejected = await SendAsync(HttpMethod.Put, $"/{Bucket}/{key}", "a replacement body"u8.ToArray(),
            Md5Base64("a completely different payload"u8.ToArray()));
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Contains("<Code>BadDigest</Code>", await rejected.Content.ReadAsStringAsync());

        var get = await SendAsync(HttpMethod.Get, $"/{Bucket}/{key}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(first, await get.Content.ReadAsByteArrayAsync());
        Assert.Equal(Md5Hex(first), get.Headers.ETag?.Tag.Trim('"'));
    }

    [Fact]
    public async Task UploadPart_WrongContentMd5_LeavesThePreviousPartIntact()
    {
        var key = Key("part");
        var first = "the part that was stored first"u8.ToArray();

        var init = await SendAsync(HttpMethod.Post, $"/{Bucket}/{key}?uploads");
        var uploadId = Regex.Match(await init.Content.ReadAsStringAsync(), "<UploadId>([^<]+)</UploadId>").Groups[1].Value;
        Assert.NotEmpty(uploadId);

        var stored = await SendAsync(HttpMethod.Put, $"/{Bucket}/{key}?partNumber=1&uploadId={uploadId}", first, Md5Base64(first));
        Assert.Equal(HttpStatusCode.OK, stored.StatusCode);

        var rejected = await SendAsync(HttpMethod.Put, $"/{Bucket}/{key}?partNumber=1&uploadId={uploadId}",
            "a replacement part"u8.ToArray(), Md5Base64("a completely different part"u8.ToArray()));
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Contains("<Code>BadDigest</Code>", await rejected.Content.ReadAsStringAsync());

        var list = await SendAsync(HttpMethod.Get, $"/{Bucket}/{key}?uploadId={uploadId}");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var body = await list.Content.ReadAsStringAsync();
        Assert.Contains($"<ETag>&quot;{Md5Hex(first)}&quot;</ETag>", body);
        Assert.Contains($"<Size>{first.Length}</Size>", body);
    }
}
