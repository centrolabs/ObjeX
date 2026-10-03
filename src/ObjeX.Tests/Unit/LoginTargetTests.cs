using ObjeX.Web.Helpers;

namespace ObjeX.Tests.Unit;

public class LoginTargetTests
{
    const string Host = "objex.example:9001";

    [Theory]
    [InlineData("/buckets/photos?prefix=2024%2F", "/buckets/photos?prefix=2024%2F")]
    [InlineData("http://objex.example:9001/buckets/photos", "/buckets/photos")]
    [InlineData("https://OBJEX.example:9001/jobs", "/jobs")]
    public void APageOnThisHost_IsFollowedAsAPath(string returnUrl, string expected) =>
        Assert.Equal(expected, LoginTarget.Local(returnUrl, Host));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("http://objex.example:9001/")]
    [InlineData("http://objex.example:9001/not-found")]
    [InlineData("http://evil.example/buckets")]
    [InlineData("http://objex.example:9002/buckets")]
    [InlineData("//evil.example/buckets")]
    [InlineData("/\\evil.example")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/\t/evil.example")]
    [InlineData("/\n/evil.example")]
    [InlineData("/\r/evil.example")]
    [InlineData("/buckets\u0000")]
    public void NoParticularPageOrAnotherHost_LeavesItToTheStartPage(string? returnUrl) =>
        Assert.Null(LoginTarget.Local(returnUrl, Host));

    [Fact]
    public void StartPage_FollowsTheCookieOnlyForAPageTheUserMayOpen()
    {
        static bool Admin(string role) => role == "Admin";
        static bool User(string role) => role == "User";

        Assert.Equal("/jobs", StartPages.Resolve("/jobs", Admin));
        Assert.Equal(StartPages.Default, StartPages.Resolve("/jobs", User));
        Assert.Equal("/buckets", StartPages.Resolve("/buckets", User));
        Assert.Equal(StartPages.Default, StartPages.Resolve("https://evil.example", Admin));
        Assert.Equal(StartPages.Default, StartPages.Resolve(null, Admin));
    }
}
