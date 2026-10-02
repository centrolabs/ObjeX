using Microsoft.AspNetCore.Http;
using ObjeX.Web.Services;

namespace ObjeX.Tests.Unit;

public class UiPreferencesTests
{
    static IRequestCookieCollection Cookies(string header)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = header;
        return context.Request.Cookies;
    }

    [Fact]
    public void HtmlClass_CarriesThemeDensityAndFont_FromTheFirstPaint()
    {
        Assert.Equal("ox-dark ox-compact ox-font-system",
            UiPreferences.HtmlClass(Cookies("objex-theme=standard-dark; objex-density=compact; objex-font=system")));
        Assert.Equal("ox-light", UiPreferences.HtmlClass(Cookies("objex-density=comfortable; objex-font=inter")));
        Assert.Equal("ox-light", UiPreferences.HtmlClass(null));
    }

    [Fact]
    public void CookiesFrom_KeepsOnlyThePreferences()
    {
        var cookies = UiPreferences.CookiesFrom(Cookies("objex-clock=12; objex-start=/jobs; .AspNetCore.Identity.Application=secret"));
        Assert.Equal(new Dictionary<string, string> { ["objex-clock"] = "12", ["objex-start"] = "/jobs" }, cookies);
    }
}
