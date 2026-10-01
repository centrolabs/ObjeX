using System.Net;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using ObjeX.Api;
using ObjeX.Core.Models;

namespace ObjeX.Tests;

/// <summary>
/// A logged-in browser on the UI port: the login cookie plus the antiforgery cookie and request token a page hands
/// to its scripts. The token is issued for the same principal the cookie carries, so the server accepts it.
/// </summary>
public sealed class UiSession
{
    public required HttpClient Client { get; init; }
    public required string TokenHeader { get; init; }
    public required string Token { get; init; }
    public required string UserId { get; init; }

    public static async Task<UiSession> LoginAsync(WebApplicationFactory<ApiAssemblyMarker> factory, string login, string password)
    {
        var jar = new CookieContainer();
        var client = factory.CreateDefaultClient(new CookieContainerHandler(jar));

        var response = await client.PostAsync("/account/login", new FormUrlEncodedContent([
            new KeyValuePair<string, string>("login", login),
            new KeyValuePair<string, string>("password", password),
            new KeyValuePair<string, string>("returnUrl", "/"),
        ]));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.DoesNotContain("error=1", response.Headers.Location?.OriginalString ?? "");

        using var scope = factory.Services.CreateScope();
        var user = (await scope.ServiceProvider.GetRequiredService<UserManager<User>>().FindByNameAsync(login))!;
        var principal = await scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<User>>().CreateAsync(user);

        var options = scope.ServiceProvider.GetRequiredService<IOptions<AntiforgeryOptions>>().Value;
        var tokens = scope.ServiceProvider.GetRequiredService<IAntiforgery>()
            .GetTokens(new DefaultHttpContext { User = principal, RequestServices = scope.ServiceProvider });
        jar.Add(client.BaseAddress!, new Cookie(options.Cookie.Name!, tokens.CookieToken));

        return new UiSession { Client = client, TokenHeader = options.HeaderName!, Token = tokens.RequestToken!, UserId = user.Id };
    }

    /// <summary>PUT with the antiforgery header, as the upload script sends it.</summary>
    public Task<HttpResponseMessage> PutAsync(string url, HttpContent content, string? token = null, CancellationToken ctk = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, url) { Content = content };
        request.Headers.Add(TokenHeader, token ?? Token);
        return Client.SendAsync(request, ctk);
    }
}
