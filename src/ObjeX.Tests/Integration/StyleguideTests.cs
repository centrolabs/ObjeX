using System.Net;

namespace ObjeX.Tests.Integration;

/// <summary>The styleguide documents the Ui library for developers. The test host runs as Production, where the route must not exist.</summary>
public class StyleguideTests(ObjeXFactory factory) : IClassFixture<ObjeXFactory>
{
    [Fact]
    public async Task Styleguide_OutsideDevelopment_Is404()
    {
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.GetAsync("/styleguide");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("styleguide", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoginPage_HasNoLinkToTheStyleguide()
    {
        var html = await factory.CreateClient().GetStringAsync("/login");

        Assert.DoesNotContain("/styleguide", html);
    }
}
