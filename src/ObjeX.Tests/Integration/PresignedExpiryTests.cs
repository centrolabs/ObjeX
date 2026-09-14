using System.Net;
using Microsoft.Extensions.DependencyInjection;
using ObjeX.Core.Models;
using ObjeX.Core.Utilities;
using ObjeX.Infrastructure.Data;

namespace ObjeX.Tests.Integration;

/// <summary>
/// X-Amz-Expires is chosen by whoever signs the URL, so the server must cap it: at the Settings
/// maximum and, like AWS, at seven days.
/// </summary>
public class PresignedExpiryTests(ObjeXFactory factory) : IClassFixture<ObjeXFactory>
{
    private const string Key = "presign-expiry.txt";
    private readonly HttpClient _s3 = factory.CreateS3Client();

    private async Task EnsureObjectAsync()
    {
        var body = "expiry"u8.ToArray();
        var put = new HttpRequestMessage(HttpMethod.Put, $"/test-bucket/{Key}") { Content = new ByteArrayContent(body) };
        S3RequestSigner.SignRequest(put, factory.AccessKeyId, factory.SecretAccessKey, body);
        Assert.Equal(HttpStatusCode.OK, (await _s3.SendAsync(put)).StatusCode);
    }

    private Task<HttpResponseMessage> GetPresignedAsync(int expiresSeconds) =>
        _s3.GetAsync(PresignedUrlGenerator.Generate("http://localhost:9000", "test-bucket", Key,
            factory.AccessKeyId, factory.SecretAccessKey, expiresSeconds));

    private async Task SetMaxExpiryAsync(int? seconds)
    {
        using var scope = factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ObjeXDbContext>();
        var settings = await db.SystemSettings.FindAsync(1);
        if (settings is null)
        {
            settings = new SystemSettings { Id = 1 };
            db.SystemSettings.Add(settings);
        }
        settings.PresignedUrlMaxExpirySeconds = seconds ?? 604800;
        await db.SaveChangesAsync();
    }

    [Theory]
    [InlineData(604801)]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task ExpiresOutsideAwsLimits_IsRejected(int expiresSeconds)
    {
        await EnsureObjectAsync();

        var response = await GetPresignedAsync(expiresSeconds);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("AuthorizationQueryParametersError", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SettingsMaximum_IsEnforcedOnClientSignedUrls()
    {
        await EnsureObjectAsync();
        await SetMaxExpiryAsync(60);
        try
        {
            var tooLong = await GetPresignedAsync(3600);
            Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
            Assert.Contains("AuthorizationQueryParametersError", await tooLong.Content.ReadAsStringAsync());

            var withinLimit = await GetPresignedAsync(30);
            Assert.Equal(HttpStatusCode.OK, withinLimit.StatusCode);
        }
        finally
        {
            await SetMaxExpiryAsync(null);
        }
    }
}
