using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using ObjeX.Infrastructure.Hashing;
using ObjeX.Infrastructure.Storage;

namespace ObjeX.Tests.Integration;

/// <summary>
/// An open multipart upload is state the database knows about; a process restart must not throw its
/// parts away. Only the weekly job, which checks the database, may clean up multipart uploads.
/// </summary>
public class MultipartRestartTests(ObjeXFactory factory) : IClassFixture<ObjeXFactory>
{
    private readonly HttpClient _s3 = factory.CreateS3Client();

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

    [Fact]
    public async Task OpenUploadOlderThanTwoDays_SurvivesARestart()
    {
        const string key = "restart/survivor.bin";
        var part = "part-one"u8.ToArray();

        var init = await SendAsync(HttpMethod.Post, $"/test-bucket/{key}?uploads");
        Assert.Equal(HttpStatusCode.OK, init.StatusCode);
        var uploadId = Regex.Match(await init.Content.ReadAsStringAsync(), "<UploadId>([^<]+)</UploadId>").Groups[1].Value;

        var put = await SendAsync(HttpMethod.Put, $"/test-bucket/{key}?partNumber=1&uploadId={uploadId}", part);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var etag = put.Headers.ETag!.Tag.Trim('"');

        // The last part was written three days ago, the upload is still open.
        var partsDir = Path.Combine(factory.BlobBasePath, "_multipart", uploadId);
        Assert.True(Directory.Exists(partsDir));
        Directory.SetLastWriteTimeUtc(partsDir, DateTime.UtcNow.AddDays(-3));

        // A restart constructs the storage service again, which runs the startup cleanup.
        _ = new FileSystemStorageService(factory.BlobBasePath, new Sha256HashService(), NullLogger<FileSystemStorageService>.Instance);

        var completeXml = Encoding.UTF8.GetBytes(
            $"<CompleteMultipartUpload><Part><PartNumber>1</PartNumber><ETag>\"{etag}\"</ETag></Part></CompleteMultipartUpload>");
        var complete = await SendAsync(HttpMethod.Post, $"/test-bucket/{key}?uploadId={uploadId}", completeXml, "application/xml");
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);

        var get = await SendAsync(HttpMethod.Get, $"/test-bucket/{key}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(part, await get.Content.ReadAsByteArrayAsync());
    }
}
