using System.Net;

namespace ObjeX.Tests.Integration;

/// <summary>The Hangfire dashboard is served to the Admin in every environment; the test host runs as Production.</summary>
public class HangfireDashboardTests(ObjeXFactory factory) : IClassFixture<ObjeXFactory>
{
    [Fact]
    public async Task Dashboard_IsServedToTheAdmin()
    {
        var client = await LoggedInAdminAsync(factory);

        var response = await client.GetAsync("/hangfire");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Hangfire Dashboard", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Dashboard_RefusesAnonymousRequests()
    {
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.GetAsync("/hangfire");

        // Not authorized for the dashboard; the status code pages turn the refusal into the redirect every browser path gets.
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/not-found", response.Headers.Location?.OriginalString);
        Assert.DoesNotContain("Hangfire", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<HttpClient> LoggedInAdminAsync(ObjeXFactory factory)
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
