using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ObjeX.Infrastructure.Data;

namespace ObjeX.Tests.Integration;

/// <summary>LastUsedAt is written at most once a minute per credential, not on every S3 request.</summary>
public class LastUsedThrottleTests(ObjeXFactory factory) : IClassFixture<ObjeXFactory>
{
    private async Task SetLastUsedAsync(DateTime? value)
    {
        using var scope = factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ObjeXDbContext>();
        await db.S3Credentials.Where(c => c.AccessKeyId == factory.AccessKeyId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.LastUsedAt, value));
    }

    private async Task<DateTime?> GetLastUsedAsync()
    {
        using var scope = factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ObjeXDbContext>();
        return await db.S3Credentials.AsNoTracking().Where(c => c.AccessKeyId == factory.AccessKeyId).Select(c => c.LastUsedAt).SingleAsync();
    }

    private async Task SendSignedRequestAsync()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        S3RequestSigner.SignRequest(request, factory.AccessKeyId, factory.SecretAccessKey);
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateS3Client().SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task RecentValue_IsNotRewritten()
    {
        var recent = DateTime.UtcNow.AddSeconds(-20);
        await SetLastUsedAsync(recent);

        await SendSignedRequestAsync();

        Assert.Equal(recent, await GetLastUsedAsync());
    }

    [Fact]
    public async Task StaleOrEmptyValue_IsRefreshed()
    {
        await SetLastUsedAsync(DateTime.UtcNow.AddMinutes(-5));
        await SendSignedRequestAsync();
        Assert.True(await GetLastUsedAsync() > DateTime.UtcNow.AddSeconds(-10));

        await SetLastUsedAsync(null);
        await SendSignedRequestAsync();
        Assert.NotNull(await GetLastUsedAsync());
    }
}
