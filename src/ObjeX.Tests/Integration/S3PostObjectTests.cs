using System.Net;
using System.Text;

namespace ObjeX.Tests.Integration;

/// <summary>
/// Presigned POST Object authenticates via form fields. The policy is the only bound on a leaked
/// signature, so a policy without a usable expiration must be rejected rather than silently accepted.
/// </summary>
public class S3PostObjectTests(ObjeXFactory factory) : IClassFixture<ObjeXFactory>
{
    private const string Bucket = "test-bucket";

    [Fact]
    public async Task ValidPolicy_UploadsTheObject()
    {
        var response = await PostAsync("post-valid.txt", PolicyJson(DateTime.UtcNow.AddMinutes(5).ToString("o")));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.NotNull(response.Headers.ETag);
    }

    [Fact]
    public async Task PolicyWithoutExpiration_IsRejected()
    {
        var response = await PostAsync("post-no-expiry.txt", PolicyJson(null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("expiration", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PolicyWithUnparsableExpiration_IsRejected()
    {
        var response = await PostAsync("post-bad-expiry.txt", PolicyJson("whenever"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("expiration", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ExpiredPolicy_IsRejected()
    {
        var response = await PostAsync("post-expired.txt", PolicyJson(DateTime.UtcNow.AddMinutes(-1).ToString("o")));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("expired", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ExpirationWithoutTimezone_IsRejectedWhenPast()
    {
        // AWS sends ISO-8601 with Z; a naive timestamp must still be read, not skipped.
        var response = await PostAsync("post-naive-expiry.txt",
            PolicyJson(DateTime.UtcNow.AddMinutes(-30).ToString("yyyy-MM-dd'T'HH:mm:ss")));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("expired", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task MalformedPolicyJson_IsRejectedWithoutServerError()
    {
        var response = await PostAsync("post-broken.txt", "{\"conditions\":");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task WrongSignature_IsRejected()
    {
        var response = await PostAsync("post-unsigned.txt",
            PolicyJson(DateTime.UtcNow.AddMinutes(5).ToString("o")), corruptSignature: true);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("SignatureDoesNotMatch", await response.Content.ReadAsStringAsync());
    }

    private async Task<HttpResponseMessage> PostAsync(string key, string policyJson, bool corruptSignature = false)
    {
        var policyB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(policyJson));
        var (credential, signature) = S3RequestSigner.SignPolicy(factory.AccessKeyId, factory.SecretAccessKey, policyB64);
        if (corruptSignature)
            signature = new string('0', signature.Length);

        var content = new MultipartFormDataContent
        {
            { new StringContent(key), "key" },
            { new StringContent(policyB64), "policy" },
            { new StringContent("AWS4-HMAC-SHA256"), "X-Amz-Algorithm" },
            { new StringContent(credential), "X-Amz-Credential" },
            { new StringContent(signature), "X-Amz-Signature" }
        };
        content.Add(new ByteArrayContent("hello"u8.ToArray()), "file", key);

        return await factory.CreateS3Client().PostAsync($"/{Bucket}", content);
    }

    private static string PolicyJson(string? expiration)
    {
        var exp = expiration is null ? "" : $"\"expiration\":\"{expiration}\",";
        return $"{{{exp}\"conditions\":[{{\"bucket\":\"{Bucket}\"}},[\"starts-with\",\"$key\",\"post-\"]]}}";
    }
}
