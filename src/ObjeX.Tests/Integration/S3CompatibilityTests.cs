using System.Net;

namespace ObjeX.Tests.Integration;

public class S3CompatibilityTests(ObjeXFactory factory) : IClassFixture<ObjeXFactory>
{
    private readonly HttpClient _client = factory.CreateS3Client();

    [Theory]
    [InlineData("versioning")]
    [InlineData("lifecycle")]
    [InlineData("policy")]
    [InlineData("cors")]
    [InlineData("encryption")]
    [InlineData("tagging")]
    [InlineData("acl")]
    public async Task UnsupportedBucketOps_Return501(string op)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/test-bucket?{op}");
        S3RequestSigner.SignRequest(request, factory.AccessKeyId, factory.SecretAccessKey);
        var response = await _client.SendAsync(request);
        Assert.Equal((HttpStatusCode)501, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("NotImplemented", body);
    }

    [Fact]
    public async Task GetBucketLocation_ReturnsUsEast1()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/test-bucket?location");
        S3RequestSigner.SignRequest(request, factory.AccessKeyId, factory.SecretAccessKey);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var xml = await response.Content.ReadAsStringAsync();
        Assert.Contains("us-east-1", xml);
    }
}
