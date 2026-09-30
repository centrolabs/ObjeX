using ObjeX.Core.Interfaces;
using ObjeX.Web.Helpers;

namespace ObjeX.Tests.Unit;

public class JobRunTextTests
{
    [Fact]
    public void Indented_DropsHangfiresTypeProperty()
    {
        var text = JobRunText.Indented("""{"$type":"ObjeX.Infrastructure.Jobs.CleanupResult, ObjeX.Infrastructure","FilesChecked":3}""");

        Assert.DoesNotContain("$type", text);
        Assert.Contains("\"FilesChecked\": 3", text);
    }

    [Fact]
    public void Indented_LeavesTextThatIsNoJson() => Assert.Equal("not json", JobRunText.Indented("not json"));

    [Fact]
    public void StateDetails_NamesTheKnownKeysWithTheirUnits()
    {
        var change = new JobStateChange("Succeeded", DateTime.UtcNow, null,
            new Dictionary<string, string> { ["Latency"] = "40", ["PerformanceDuration"] = "250", ["Result"] = "{}" });

        Assert.Equal("Latency 40 ms · Duration 250 ms", JobRunText.StateDetails(change));
    }

    [Theory]
    [InlineData(JobRunState.Failed, true, true)]
    [InlineData(JobRunState.Retrying, true, true)]
    [InlineData(JobRunState.Succeeded, false, true)]
    [InlineData(JobRunState.Queued, false, true)]
    [InlineData(JobRunState.Processing, false, false)]
    public void Actions_FollowTheState(JobRunState state, bool retry, bool delete)
    {
        Assert.Equal(retry, JobRunText.CanRetry(state));
        Assert.Equal(delete, JobRunText.CanDelete(state));
    }
}
