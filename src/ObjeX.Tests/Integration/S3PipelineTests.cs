using System.Net;

namespace ObjeX.Tests.Integration;

/// <summary>
/// The S3 API is selected by the port a request arrives on, never by the Host header, and it is
/// isolated from the UI pipeline in both directions.
/// </summary>
public class S3PipelineTests(ObjeXFactory factory) : IClassFixture<ObjeXFactory>
{
    private readonly HttpClient _s3 = factory.CreateS3Client();

    [Fact]
    public async Task SignedRequest_HostHeaderWithoutPort_IsRouted()
    {
        // A reverse proxy or ingress on 443 forwards "Host: s3.example.com" with no port.
        // The S3 client signs that Host, so the proxy cannot rewrite it either.
        var request = new HttpRequestMessage(HttpMethod.Get, "/test-bucket?location");
        request.Headers.Host = "s3.example.com";
        S3RequestSigner.SignRequest(request, factory.AccessKeyId, factory.SecretAccessKey);

        var response = await _s3.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("LocationConstraint", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task HeadBucket_Missing_Returns404_NotRedirect()
    {
        // HeadBucket answers with an empty body. The UI's status-code redirect must never see it.
        var request = new HttpRequestMessage(HttpMethod.Head, "/no-such-bucket-xyz");
        S3RequestSigner.SignRequest(request, factory.AccessKeyId, factory.SecretAccessKey);

        var response = await _s3.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task UiPath_OnS3Port_IsInterpretedAsS3()
    {
        // "/health" on the S3 port is a GetBucket for a bucket named "health", nothing else.
        var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        S3RequestSigner.SignRequest(request, factory.AccessKeyId, factory.SecretAccessKey);

        var response = await _s3.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/xml", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("NoSuchBucket", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task S3Path_OnUiPort_IsNotServedByS3()
    {
        var ui = factory.CreateClient(new() { AllowAutoRedirect = false });
        var request = new HttpRequestMessage(HttpMethod.Get, "/test-bucket?location");
        S3RequestSigner.SignRequest(request, factory.AccessKeyId, factory.SecretAccessKey);

        var response = await ui.SendAsync(request);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual("application/xml", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task UnsignedRequest_OnS3Port_GetsS3Error_NotLoginRedirect()
    {
        var response = await _s3.GetAsync("/test-bucket");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/xml", response.Content.Headers.ContentType?.MediaType);
        Assert.Null(response.Headers.Location);
    }
}
