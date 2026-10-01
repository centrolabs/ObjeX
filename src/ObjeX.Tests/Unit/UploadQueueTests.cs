using ObjeX.Web.Helpers;

namespace ObjeX.Tests.Unit;

public class UploadQueueTests
{
    private static UploadFile File(int id, string path, long size = 100, string? type = "text/plain") => new(id, path, size, type);

    [Theory]
    [InlineData("", "a.txt", "a.txt")]
    [InlineData("photos/2024/", "a.jpg", "photos/2024/a.jpg")]
    [InlineData("photos/", "trip/day 1/a.jpg", "photos/trip/day 1/a.jpg")]
    [InlineData("photos/", "/trip/a.jpg", "photos/trip/a.jpg")]
    public void KeyFor_PutsTheRelativePathUnderThePrefix(string prefix, string path, string expected)
        => Assert.Equal(expected, UploadQueue.KeyFor(prefix, path));

    [Fact]
    public void Add_QueuesValidKeys_AndFailsARefusedKeyAtOnce()
    {
        var queue = new UploadQueue();

        var toSend = queue.Add("docs/", [File(1, "a.txt"), File(2, "bad\u0001name.txt"), File(3, "sub/b.txt", type: "")]);

        Assert.Equal([1, 3], toSend.Select(i => i.Id));
        Assert.Equal(["docs/a.txt", "docs/sub/b.txt"], toSend.Select(i => i.Key));
        Assert.Equal("application/octet-stream", toSend[1].ContentType);
        var refused = queue.Items.Single(i => i.Id == 2);
        Assert.Equal(UploadStatus.Failed, refused.Status);
        Assert.Equal("Object key must not contain control characters", refused.Error);
        Assert.False(refused.CanRetry);
        Assert.True(queue.IsActive);
    }

    [Fact]
    public void Apply_MovesFilesThroughTheirStates()
    {
        var queue = new UploadQueue();
        queue.Add("", [File(1, "a", 100), File(2, "b", 200), File(3, "c", 300)]);

        queue.Apply([new(1, "progress", Loaded: 40), new(2, "done"), new(3, "failed", Status: 507, Error: "Quota")]);

        var (a, b, c) = (queue.Items[0], queue.Items[1], queue.Items[2]);
        Assert.Equal((UploadStatus.Uploading, 40L), (a.Status, a.Loaded));
        Assert.Equal((UploadStatus.Done, 200L), (b.Status, b.Loaded));
        Assert.Equal((UploadStatus.Failed, "Quota"), (c.Status, c.Error));
        Assert.True(c.CanRetry);
        Assert.Equal(240, queue.SentBytes);
        Assert.Equal(600, queue.TotalBytes);
        Assert.Equal(40, queue.Percent);
    }

    [Fact]
    public void Apply_IgnoresProgressAfterTheEnd_AndUnknownIds()
    {
        var queue = new UploadQueue();
        queue.Add("", [File(1, "a")]);

        queue.Apply([new(1, "cancelled"), new(1, "progress", Loaded: 50), new(99, "done")]);

        Assert.Equal(UploadStatus.Cancelled, queue.Items.Single().Status);
        Assert.False(queue.IsActive);
    }

    [Fact]
    public void Retry_PutsFailedAndCancelledFilesBack_ButNotARefusedKey()
    {
        var queue = new UploadQueue();
        queue.Add("", [File(1, "a"), File(2, "b"), File(3, "c"), File(4, "/")]);
        queue.Apply([new(1, "failed", Status: 0), new(2, "cancelled"), new(3, "done")]);

        Assert.Equal([1], queue.Retry(1).Select(i => i.Id));
        Assert.Equal(UploadStatus.Queued, queue.Items[0].Status);
        Assert.Null(queue.Items[0].Error);

        Assert.Equal([2], queue.Retry().Select(i => i.Id));
        Assert.Equal(UploadStatus.Failed, queue.Items[3].Status);
    }

    [Fact]
    public void CancelledFiles_LeaveTheTotals()
    {
        var queue = new UploadQueue();
        queue.Add("", [File(1, "a", 100), File(2, "b", 300)]);

        queue.Apply([new(1, "done"), new(2, "cancelled")]);

        Assert.Equal(100, queue.TotalBytes);
        Assert.Equal(100, queue.Percent);
    }

    [Fact]
    public void Percent_OfEmptyFiles_CountsFinishedFiles()
    {
        var queue = new UploadQueue();
        queue.Add("", [File(1, "a", 0), File(2, "b", 0)]);

        queue.Apply([new(1, "done")]);

        Assert.Equal(50, queue.Percent);
    }

    [Fact]
    public void Clear_EmptiesTheQueue_AndReturnsTheIdsToForget()
    {
        var queue = new UploadQueue();
        queue.Add("", [File(1, "a"), File(2, "b")]);
        queue.Apply([new(1, "done"), new(2, "failed")]);

        Assert.Equal([1, 2], queue.Clear());
        Assert.Empty(queue.Items);
    }
}
