using ObjeX.Web.Helpers;

namespace ObjeX.Tests.Unit;

public class AppVersionTests
{
    [Fact]
    public void Version_Is_Stamped_And_Not_The_Sdk_Default()
    {
        Assert.NotEqual("1.0.0", AppVersion.Version);
        Assert.Matches(@"^\d+\.\d+\.\d+$", AppVersion.Version);
    }

    [Fact]
    public void Display_Starts_With_Product_And_Version()
        => Assert.StartsWith($"ObjeX {AppVersion.Version}", AppVersion.Display);
}
