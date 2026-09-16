using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using ObjeX.Api.Options;
using ObjeX.Core.Models;

namespace ObjeX.Tests.Integration;

/// <summary>
/// "Stay signed in" only changes the cookie's lifetime: ticked, it is persistent and expires after
/// Auth:RememberMeDays; unticked, it is a session cookie the browser drops on close.
/// </summary>
public class RememberMeTests(ObjeXFactory factory) : IClassFixture<ObjeXFactory>
{
    private const string Password = "correct-horse";
    private static readonly string CookieName = $".AspNetCore.{IdentityConstants.ApplicationScheme}";

    private int RememberMeDays => factory.Services.GetRequiredService<IOptions<AuthOptions>>().Value.RememberMeDays;

    private async Task<HttpClient> ClientWithUserAsync(string username)
    {
        using var scope = factory.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        if (await userManager.FindByNameAsync(username) is null)
        {
            var user = new User { UserName = username, Email = $"{username}@test.local", EmailConfirmed = true };
            var result = await userManager.CreateAsync(user, Password);
            Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.Description)));
        }
        return factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
    }

    private static async Task<SetCookieHeaderValue> LoginAsync(HttpClient client, string username, bool rememberMe)
    {
        List<KeyValuePair<string, string>> fields = [new("login", username), new("password", Password)];
        if (rememberMe) fields.Add(new("rememberMe", "true"));

        var response = await client.PostAsync("/account/login", new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);

        var cookies = SetCookieHeaderValue.ParseList(response.Headers.GetValues(HeaderNames.SetCookie).ToList());
        return Assert.Single(cookies, c => c.Name == CookieName);
    }

    [Fact]
    public async Task RememberMe_IssuesAPersistentCookieExpiringAfterTheConfiguredDays()
    {
        var client = await ClientWithUserAsync("remember-yes");

        var cookie = await LoginAsync(client, "remember-yes", rememberMe: true);

        Assert.NotNull(cookie.Expires);
        var drift = cookie.Expires!.Value - DateTimeOffset.UtcNow.AddDays(RememberMeDays);
        Assert.True(Math.Abs(drift.TotalMinutes) < 5, $"expires drifted by {drift.TotalMinutes} minutes");
    }

    [Fact]
    public async Task WithoutRememberMe_IssuesASessionCookie()
    {
        var client = await ClientWithUserAsync("remember-no");

        var cookie = await LoginAsync(client, "remember-no", rememberMe: false);

        Assert.Null(cookie.Expires);
        Assert.Null(cookie.MaxAge);
    }
}
