using System.Net;
using System.Net.Http.Headers;

namespace ObjeX.Tests.Integration;

public class ObjectDownloadTests(ObjeXFactory factory) : IClassFixture<ObjeXFactory>
{
    private readonly HttpClient _s3 = factory.CreateS3Client();

    [Theory]
    [InlineData("text/html", "<script>alert(1)</script>")]
    [InlineData("image/svg+xml", "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>")]
    public async Task UnsafeContentType_ServedAsAttachment(string contentType, string body)
    {
        var key = await UploadAsync(contentType, body);
        var client = await CreateLoggedInUiClientAsync();

        var response = await client.GetAsync($"/api/objects/test-bucket/{key}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/octet-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("attachment", response.Content.Headers.ContentDisposition?.ToString());
        Assert.Equal(key, response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.False(response.Headers.Contains("Content-Security-Policy"));
    }

    [Fact]
    public async Task PlainText_ServedInlineWithSandbox()
    {
        var key = await UploadAsync("text/plain; charset=utf-8", "hello world");
        var client = await CreateLoggedInUiClientAsync();

        var response = await client.GetAsync($"/api/objects/test-bucket/{key}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Parameters must be stripped before the allowlist comparison.
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Null(response.Content.Headers.ContentDisposition);
        Assert.Equal("sandbox", response.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Theory]
    [InlineData("text/html", "<script>alert(1)</script>")]
    [InlineData("text/plain", "hello world")]
    public async Task DownloadFlag_AlwaysYieldsAttachment(string contentType, string body)
    {
        var key = await UploadAsync(contentType, body);
        var client = await CreateLoggedInUiClientAsync();

        var response = await client.GetAsync($"/api/objects/test-bucket/{key}?download=true");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/octet-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("attachment", response.Content.Headers.ContentDisposition?.ToString());
    }

    private async Task<string> UploadAsync(string contentType, string body)
    {
        var key = $"xss-{Guid.NewGuid():N}.bin";
        var bytes = System.Text.Encoding.UTF8.GetBytes(body);

        var request = new HttpRequestMessage(HttpMethod.Put, $"/test-bucket/{key}")
        {
            Content = new ByteArrayContent(bytes),
        };
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        S3RequestSigner.SignRequest(request, factory.AccessKeyId, factory.SecretAccessKey, bytes);

        var response = await _s3.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return key;
    }

    private async Task<HttpClient> CreateLoggedInUiClientAsync()
    {
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        var login = await client.PostAsync("/account/login", new FormUrlEncodedContent([
            new KeyValuePair<string, string>("login", "admin"),
            new KeyValuePair<string, string>("password", "admin"),
            new KeyValuePair<string, string>("returnUrl", "/"),
        ]));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        return client;
    }
}
