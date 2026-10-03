using ObjeX.Web.Helpers;

namespace ObjeX.Tests.Unit;

public class RelativeTimeTests
{
    static readonly DateTime Now = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(0, "just now")]
    [InlineData(59, "just now")]
    [InlineData(60, "1 min ago")]
    [InlineData(59 * 60 + 59, "59 min ago")]
    [InlineData(3600, "1 hour ago")]
    [InlineData(2 * 3600 - 1, "1 hour ago")]
    [InlineData(5 * 3600, "5 hours ago")]
    [InlineData(86400, "1 day ago")]
    [InlineData(6 * 86400 + 86399, "6 days ago")]
    public void Past(int secondsAgo, string expected) =>
        Assert.Equal(expected, RelativeTime.Format(Now.AddSeconds(-secondsAgo), Now));

    [Theory]
    [InlineData(30, "just now")]
    [InlineData(12 * 60, "in 12 min")]
    [InlineData(3 * 3600, "in 3 hours")]
    [InlineData(2 * 86400, "in 2 days")]
    public void Ahead(int secondsAhead, string expected) =>
        Assert.Equal(expected, RelativeTime.Format(Now.AddSeconds(secondsAhead), Now));

    [Fact]
    public void AWeekOrMore_LeavesTheTimestampToTheCaller()
    {
        Assert.Null(RelativeTime.Format(Now.AddDays(-7), Now));
        Assert.Null(RelativeTime.Format(Now.AddDays(30), Now));
    }

    // SQLite hands timestamps back with Kind Unspecified; they are UTC all the same.
    [Fact]
    public void AnUnspecifiedKind_IsReadAsUtc() =>
        Assert.Equal("2 hours ago", RelativeTime.Format(DateTime.SpecifyKind(Now.AddHours(-2), DateTimeKind.Unspecified), Now));

    [Fact]
    public void Changes_OnlyWithinAWeek()
    {
        Assert.True(RelativeTime.Changes(Now.AddMinutes(-5), Now));
        Assert.True(RelativeTime.Changes(Now.AddDays(3), Now));
        Assert.False(RelativeTime.Changes(Now.AddDays(-8), Now));
    }
}
