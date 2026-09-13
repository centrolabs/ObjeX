using System.Net;
using System.Text.RegularExpressions;

namespace ObjeX.Tests.Integration;

/// <summary>
/// The UI components live in a Razor class library; the host page, wwwroot and the framework
/// script are served by ObjeX.Api. Every asset the login page references must resolve.
/// </summary>
public class StaticAssetsTests(ObjeXFactory factory) : IClassFixture<ObjeXFactory>
{
    private static readonly Regex AssetUrl = new("(?:href|src)=\"(?<url>[^\"]+)\"", RegexOptions.Compiled);

    [Fact]
    public async Task LoginPage_ReferencesOnlyAssetsThatResolve()
    {
        var client = factory.CreateClient();
        var html = await client.GetStringAsync("/login");

        var urls = AssetUrl.Matches(html)
            .Select(m => m.Groups["url"].Value)
            .Where(u => !u.StartsWith("http", StringComparison.Ordinal) && !u.StartsWith('#'))
            .Where(u => !u.StartsWith("/login", StringComparison.Ordinal) && !u.StartsWith("/account", StringComparison.Ordinal))
            .Distinct()
            .ToList();

        Assert.Contains(urls, u => u.Contains("_framework/blazor.web") && u.EndsWith(".js")); // fingerprinted: blazor.web.<hash>.js
        Assert.Contains(urls, u => u.Contains("ObjeX.Api") && u.EndsWith(".css"));
        Assert.Contains(urls, u => u.Contains("_content/ObjeX.Web/"));

        foreach (var url in urls)
        {
            var response = await client.GetAsync(url);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{url} -> {(int)response.StatusCode}");
        }
    }

    [Fact]
    public async Task ScopedCssBundle_ContainsTheLibraryComponentsStyles()
    {
        // MainLayout.razor.css etc. live in the class library; the host bundle must carry them,
        // otherwise every scoped style silently disappears.
        var client = factory.CreateClient();
        var html = await client.GetStringAsync("/login");
        var bundleUrl = AssetUrl.Matches(html).Select(m => m.Groups["url"].Value)
            .Single(u => u.Contains("ObjeX.Api") && u.EndsWith(".css"));

        // The host bundle only imports the library bundle; the scoped rules live in the imported file.
        var hostBundle = await client.GetStringAsync(bundleUrl);
        var import = Regex.Match(hostBundle, @"@import\s+'(?<url>[^']+)'");
        Assert.True(import.Success, $"host bundle has no @import: {hostBundle}");
        Assert.StartsWith("_content/ObjeX.Web/", import.Groups["url"].Value);

        var libraryBundle = await client.GetStringAsync(import.Groups["url"].Value);
        Assert.Matches(@"\[b-[a-z0-9]+\]", libraryBundle);
    }

    [Theory]
    [InlineData("/_content/ObjeX.Web/Components/Pages/Objects.razor.js")]
    [InlineData("/_content/ObjeX.Web/Components/Dialogs/UploadObjectDialog.razor.js")]
    [InlineData("/fonts/inter-400.ttf")]
    public async Task AssetsLoadedAtRuntime_Resolve(string url)
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
