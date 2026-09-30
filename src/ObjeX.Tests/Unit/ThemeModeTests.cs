using ObjeX.Web.Services;

namespace ObjeX.Tests.Unit;

public class ThemeModeTests
{
    [Theory]
    [InlineData("standard", false)]
    [InlineData("standard-dark", true)]
    [InlineData("material", false)]       // written before the design system
    [InlineData("material-dark", true)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("something-else", false)]
    public void CookieValue_MapsToAMode(string? cookie, bool dark)
    {
        Assert.Equal(dark, ThemeMode.IsDark(cookie));
        Assert.Equal(dark ? "standard-dark" : "standard", ThemeMode.Radzen(cookie));
        Assert.Equal(dark ? "ox-dark" : "ox-light", ThemeMode.CssClass(cookie));
    }

    [Fact]
    public void WrittenValue_ReadsBackAsTheSameMode()
    {
        Assert.True(ThemeMode.IsDark(ThemeMode.CookieValue(true)));
        Assert.False(ThemeMode.IsDark(ThemeMode.CookieValue(false)));
    }
}
