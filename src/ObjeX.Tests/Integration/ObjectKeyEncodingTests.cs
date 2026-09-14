using System.Net;
using System.Text;
using ObjeX.Web.Helpers;

namespace ObjeX.Tests.Integration;

/// <summary>Object keys may contain URL syntax characters; the UI links must encode them so the same object comes back.</summary>
public class ObjectKeyEncodingTests(ObjeXFactory factory) : IClassFixture<ObjeXFactory>
{
    private async Task<byte[]> UploadViaS3Async(string key)
    {
        var body = Encoding.UTF8.GetBytes($"content of {key}");
        var path = "/test-bucket/" + string.Join("/", key.Split('/').Select(Uri.EscapeDataString));
        var put = new HttpRequestMessage(HttpMethod.Put, path) { Content = new ByteArrayContent(body) };
        S3RequestSigner.SignRequest(put, factory.AccessKeyId, factory.SecretAccessKey, body);
        var response = await factory.CreateS3Client().SendAsync(put);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return body;
    }

    private async Task<HttpClient> LoginAsAdminAsync()
    {
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var login = await client.PostAsync("/account/login", new FormUrlEncodedContent([
            new KeyValuePair<string, string>("login", "admin"),
            new KeyValuePair<string, string>("password", "admin"),
        ]));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        return client;
    }

    [Theory]
    [InlineData("enc/report#final.pdf")]
    [InlineData("enc/a?b.txt")]
    [InlineData("enc/100%.txt")]
    [InlineData("enc/with space.txt")]
    [InlineData("enc/q&a.txt")]
    [InlineData("enc/ünïcödé.txt")]
    public async Task UiDownloadLink_ReturnsTheObjectWithThatKey(string key)
    {
        var body = await UploadViaS3Async(key);
        var ui = await LoginAsAdminAsync();

        var response = await ui.GetAsync(FileHelper.ObjectUrl("test-bucket", key, download: true));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(body, await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task RawKeyInUrl_LosesEverythingAfterTheHash()
    {
        await UploadViaS3Async("raw/report#final.pdf");
        var ui = await LoginAsAdminAsync();

        // The browser (and HttpClient) never sends a fragment, so the server only ever sees "raw/report".
        var raw = await ui.GetAsync("/api/objects/test-bucket/raw/report#final.pdf");
        var encoded = await ui.GetAsync(FileHelper.ObjectUrl("test-bucket", "raw/report#final.pdf"));

        Assert.Equal(HttpStatusCode.NotFound, raw.StatusCode);
        Assert.Equal(HttpStatusCode.OK, encoded.StatusCode);
    }
}
