using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;

namespace ObjeX.Tests.Integration;

/// <summary>/metrics is open on the UI port unless Metrics:Token is set; then scrapers must send it as a Bearer token.</summary>
public class MetricsEndpointTests(MetricsEndpointTests.OpenFactory open, MetricsEndpointTests.TokenFactory token)
    : IClassFixture<MetricsEndpointTests.OpenFactory>, IClassFixture<MetricsEndpointTests.TokenFactory>
{
    public class OpenFactory : ObjeXFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Metrics:Enabled", "true");
        }
    }

    public class TokenFactory : ObjeXFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Metrics:Enabled", "true");
            builder.UseSetting("Metrics:Token", "scrape-secret");
        }
    }

    [Fact]
    public async Task WithoutToken_MetricsAreOpen()
    {
        var response = await open.CreateClient().GetAsync("/metrics");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("objex_storage_bytes", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task WithToken_MissingOrWrongBearer_IsUnauthorized()
    {
        var client = token.CreateClient(new() { AllowAutoRedirect = false });

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/metrics")).StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "wrong");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/metrics")).StatusCode);
    }

    [Fact]
    public async Task WithToken_CorrectBearer_IsServed()
    {
        var client = token.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "scrape-secret");

        var response = await client.GetAsync("/metrics");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("objex_storage_bytes", await response.Content.ReadAsStringAsync());
    }
}
