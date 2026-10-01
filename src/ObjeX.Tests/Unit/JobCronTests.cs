using ObjeX.Api.Jobs;

namespace ObjeX.Tests.Unit;

public class JobCronTests
{
    private static readonly DateTime Now = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Check_GivesTheNextRunInUtc()
    {
        var check = JobCron.Check("0 4 * * 0", "Europe/Zurich", Now);

        Assert.Null(check.Error);
        Assert.Equal(new DateTime(2026, 10, 4, 2, 0, 0, DateTimeKind.Utc), check.NextRun);
    }

    [Fact]
    public void Check_KeepsTheLocalTimeAcrossDaylightSavingTime()
    {
        var check = JobCron.Check("0 4 * * 0", "Europe/Zurich", new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(new DateTime(2026, 10, 25, 3, 0, 0, DateTimeKind.Utc), check.NextRun);
    }

    [Theory]
    [InlineData("61 * * * *")]
    [InlineData("0 0 4 * * 0")]
    [InlineData("every sunday")]
    [InlineData("")]
    [InlineData("0 0 31 2 *")]
    public void Check_RejectsAnExpressionThatIsInvalidOrNeverRuns(string cron)
    {
        var check = JobCron.Check(cron, "UTC", Now);

        Assert.NotNull(check.Error);
        Assert.Null(check.NextRun);
    }

    [Fact]
    public void Check_RejectsAnUnknownTimeZone()
    {
        var check = JobCron.Check("0 4 * * 0", "Mars/Olympus_Mons", Now);

        Assert.NotNull(check.Error);
        Assert.Null(check.NextRun);
    }
}
