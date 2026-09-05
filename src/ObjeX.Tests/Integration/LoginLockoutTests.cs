using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using ObjeX.Core.Models;

namespace ObjeX.Tests.Integration;

/// <summary>
/// Login protection is per-account lockout (Auth:Lockout), not IP rate limiting.
/// The factory configures 5 attempts / 5 minutes.
/// </summary>
public class LoginLockoutTests(ObjeXFactory factory) : IClassFixture<ObjeXFactory>
{
    private const int MaxFailedAttempts = 5;
    private const string Password = "correct-horse";

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
        return factory.CreateClient(new() { AllowAutoRedirect = false });
    }

    private static async Task<string> LoginAsync(HttpClient client, string username, string password)
    {
        var response = await client.PostAsync("/account/login", new FormUrlEncodedContent([
            new("login", username),
            new("password", password),
        ]));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return Uri.UnescapeDataString(response.Headers.Location?.OriginalString ?? "");
    }

    [Fact]
    public async Task FailuresBelowThreshold_DoNotBlockTheCorrectPassword()
    {
        const string user = "lockout-below";
        var client = await ClientWithUserAsync(user);

        for (var i = 0; i < MaxFailedAttempts - 1; i++)
            Assert.Contains("error=1", await LoginAsync(client, user, "wrong"));

        Assert.Equal("/", await LoginAsync(client, user, Password));
    }

    [Fact]
    public async Task FailuresAtThreshold_LockTheAccount_EvenForTheCorrectPassword()
    {
        const string user = "lockout-at";
        var client = await ClientWithUserAsync(user);

        for (var i = 0; i < MaxFailedAttempts; i++)
            Assert.Contains("error=1", await LoginAsync(client, user, "wrong"));

        var location = await LoginAsync(client, user, Password);
        Assert.Contains("error=1", location);
        Assert.Contains("locked", location);
    }

    [Fact]
    public async Task Lockout_IsPerAccount_OtherAccountsStillLogIn()
    {
        const string locked = "lockout-victim";
        const string other = "lockout-bystander";
        var client = await ClientWithUserAsync(locked);
        await ClientWithUserAsync(other);

        for (var i = 0; i <= MaxFailedAttempts; i++)
            await LoginAsync(client, locked, "wrong");

        Assert.Equal("/", await LoginAsync(client, other, Password));
    }
}
