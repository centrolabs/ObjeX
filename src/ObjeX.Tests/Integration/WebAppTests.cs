using System.Net;

namespace ObjeX.Tests.Integration;

public class WebAppTests(ObjeXFactory factory) : IClassFixture<ObjeXFactory>
{
    [Fact]
    public async Task Health_Liveness_ReturnsOk()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Health_Readiness_ReturnsOk()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SecurityHeaders_Present()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/health");

        Assert.True(response.Headers.Contains("X-Content-Type-Options"));
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").First());

        Assert.True(response.Headers.Contains("X-Frame-Options"));
        Assert.Equal("SAMEORIGIN", response.Headers.GetValues("X-Frame-Options").First());

        Assert.True(response.Headers.Contains("Referrer-Policy"));

        // Server header should be absent (Kestrel AddServerHeader = false)
        Assert.False(response.Headers.Contains("Server"));
    }

    [Fact]
    public async Task ApiPath_Returns401_NotRedirect()
    {
        var client = factory.CreateClient(new()
        {
            AllowAutoRedirect = false // don't follow redirects
        });
        var response = await client.GetAsync("/api/objects/test-bucket/nonexistent.txt");
        // Should be 401, not 302 redirect to /login
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CookieAuth_LoginWithValidCredentials()
    {
        var client = factory.CreateClient(new()
        {
            AllowAutoRedirect = false
        });

        var formContent = new FormUrlEncodedContent([
            new("login", "admin"),
            new("password", "admin"),
            new("returnUrl", "/"),
        ]);

        var response = await client.PostAsync("/account/login", formContent);
        // Successful login redirects (302) to returnUrl
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);

        // Response should set a cookie
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var cookies));
        Assert.Contains(cookies, c => c.Contains(".AspNetCore.Identity.Application"));
    }

    [Fact]
    public async Task CookieAuth_LoginWithBadPassword_RedirectsBack()
    {
        var client = factory.CreateClient(new()
        {
            AllowAutoRedirect = false
        });

        var formContent = new FormUrlEncodedContent([
            new("login", "admin"),
            new("password", "wrongpassword"),
        ]);

        var response = await client.PostAsync("/account/login", formContent);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location?.OriginalString ?? "";
        Assert.Contains("error=1", location);
    }

    [Fact]
    public async Task Logout_RedirectsToLogin()
    {
        var client = factory.CreateClient(new()
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/account/logout");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location?.OriginalString ?? "";
        Assert.Contains("/login", location);
    }
}
