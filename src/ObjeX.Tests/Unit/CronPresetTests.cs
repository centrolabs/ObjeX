using ObjeX.Web.Helpers;

namespace ObjeX.Tests.Unit;

public class CronPresetTests
{
    [Fact]
    public void Daily_IsMinuteAndHourOfEveryDay()
        => Assert.Equal("30 2 * * *", CronPreset.ToCron(new JobPreset(JobFrequency.Daily, DayOfWeek.Monday, new TimeOnly(2, 30))));

    [Fact]
    public void Weekly_AddsTheDay()
        => Assert.Equal("0 4 * * 0", CronPreset.ToCron(new JobPreset(JobFrequency.Weekly, DayOfWeek.Sunday, new TimeOnly(4, 0))));

    [Theory]
    [InlineData("0 3 * * 0", JobFrequency.Weekly, DayOfWeek.Sunday, 3, 0)]
    [InlineData("15 22 * * 7", JobFrequency.Weekly, DayOfWeek.Sunday, 22, 15)]
    [InlineData("30 3 * * *", JobFrequency.Daily, DayOfWeek.Sunday, 3, 30)]
    public void Read_TakesDayAndTimeFromTheCron_WithoutANextRun(string cron, JobFrequency frequency, DayOfWeek day, int hour, int minute)
        => Assert.Equal(new JobPreset(frequency, day, new TimeOnly(hour, minute)), CronPreset.Read(cron, null));

    [Fact]
    public void Read_TakesDayAndTimeFromTheNextRunInTheBrowserZone()
        => Assert.Equal(new JobPreset(JobFrequency.Weekly, DayOfWeek.Saturday, new TimeOnly(17, 0)),
            CronPreset.Read("0 3 * * 0", new DateTime(2026, 10, 3, 17, 0, 0)));

    [Theory]
    [InlineData("*/5 * * * *")]
    [InlineData("0 3 1 * *")]
    [InlineData("0 3 * * 1-5")]
    [InlineData("garbage")]
    public void Read_LeavesEverythingElseCustom(string cron)
        => Assert.Equal(JobFrequency.Custom, CronPreset.Read(cron, null).Frequency);
}
