using System.Net;

namespace ObjeX.Tests.Integration;

public class S3CorsTests(ObjeXFactory factory) : IClassFixture<ObjeXFactory>
{
    [Fact]
    public async Task BrowserRequest_CanReadTheETag()
    {
        // Browser SDKs need the ETag of every UploadPart response; without the expose header the browser hides it.
        var client = factory.CreateS3Client();
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("Origin", "https://app.example");
        S3RequestSigner.SignRequest(request, factory.AccessKeyId, factory.SecretAccessKey);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("*", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Contains("ETag", response.Headers.GetValues("Access-Control-Expose-Headers").Single());
    }
}
