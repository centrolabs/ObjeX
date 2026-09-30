using ObjeX.Web.Helpers;
using ObjeX.Web.Services;

namespace ObjeX.Tests.Unit;

public class CronTextTests
{
    static BrowserTimeZone Zone(string id)
    {
        var tz = new BrowserTimeZone();
        tz.Set(id);
        return tz;
    }

    [Theory]
    [InlineData("2026-10-04T03:00:00Z", "Every Sunday at 05:00")] // CEST
    [InlineData("2026-11-01T03:00:00Z", "Every Sunday at 04:00")] // CET
    public void Weekly_UsesTheNextRunInTheBrowserZone(string next, string expected)
        => Assert.Equal(expected, CronText.Describe("0 3 * * 0", DateTime.Parse(next).ToUniversalTime(), Zone("Europe/Zurich")));

    [Fact]
    public void Weekly_MovesToAnotherDay_WhenTheZoneCrossesMidnight()
        => Assert.Equal("Every Saturday at 17:00",
            CronText.Describe("0 3 * * 0", new DateTime(2026, 10, 4, 3, 0, 0, DateTimeKind.Utc), Zone("Pacific/Honolulu")));

    [Fact]
    public void Daily_IsEveryDay()
        => Assert.Equal("Every day at 03:30",
            CronText.Describe("30 3 * * *", new DateTime(2026, 10, 4, 3, 30, 0, DateTimeKind.Utc), new BrowserTimeZone()));

    [Fact]
    public void WithoutNextRun_ReadsTheCronAsUtc()
        => Assert.Equal("Every Sunday at 03:00 UTC", CronText.Describe("0 3 * * 0", null, Zone("Europe/Zurich")));

    [Theory]
    [InlineData("*/5 * * * *")]
    [InlineData("0 3 1 * *")]
    [InlineData("0 3 * * 1-5")]
    [InlineData("0 3 * * 9")]
    [InlineData("garbage")]
    public void OtherCrons_HaveNoWords(string cron)
        => Assert.Null(CronText.Describe(cron, DateTime.UtcNow, new BrowserTimeZone()));
}
