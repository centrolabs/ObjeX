using System.Net;
using Microsoft.AspNetCore.Hosting;

namespace ObjeX.Tests.Integration;

/// <summary>The Hangfire dashboard is a Development tool; the Jobs page replaces it everywhere else.</summary>
public class HangfireDashboardTests(ObjeXFactory factory) : IClassFixture<ObjeXFactory>
{
    [Fact]
    public async Task Dashboard_OutsideDevelopment_IsNotServed_EvenToTheAdmin()
    {
        var client = await LoggedInAdminAsync(factory);

        var response = await client.GetAsync("/hangfire");

        // No endpoint matches, so the status code pages send the browser to the not-found page.
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/not-found", response.Headers.Location?.OriginalString);
        Assert.DoesNotContain("Hangfire", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    internal static async Task<HttpClient> LoggedInAdminAsync(ObjeXFactory factory)
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

public class HangfireDashboardDevelopmentTests(HangfireDashboardDevelopmentTests.DevelopmentFactory factory)
    : IClassFixture<HangfireDashboardDevelopmentTests.DevelopmentFactory>
{
    public class DevelopmentFactory : ObjeXFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseEnvironment("Development");
        }
    }

    [Fact]
    public async Task Dashboard_InDevelopment_IsServedToTheAdmin()
    {
        var client = await HangfireDashboardTests.LoggedInAdminAsync(factory);

        var response = await client.GetAsync("/hangfire");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Hangfire Dashboard", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Dashboard_InDevelopment_RefusesAnonymousRequests()
    {
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.GetAsync("/hangfire");

        // The dashboard answers 401; the status code pages turn it into the redirect every browser path gets.
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/not-found", response.Headers.Location?.OriginalString);
    }
}
