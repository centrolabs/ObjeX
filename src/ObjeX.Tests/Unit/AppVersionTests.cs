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
    public void Display_Is_Dev_For_A_Local_Build_And_The_Version_Otherwise()
        => Assert.StartsWith(AppVersion.Version == "0.0.0" ? "ObjeX dev" : $"ObjeX {AppVersion.Version}", AppVersion.Display);

    [Theory]
    [InlineData("0.0.0", "92b7eb4c0ffee", "ObjeX dev (92b7eb4)")]
    [InlineData("0.0.0", "", "ObjeX dev")]
    [InlineData("1.2.5", "92b7eb4c0ffee", "ObjeX 1.2.5 (92b7eb4)")]
    [InlineData("1.2.5", "", "ObjeX 1.2.5")]
    public void Format_Shows_Dev_Only_For_0_0_0(string version, string sha, string expected)
        => Assert.Equal(expected, AppVersion.Format(version, sha));
}
