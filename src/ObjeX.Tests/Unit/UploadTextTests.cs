using ObjeX.Web.Components.Ui;
using ObjeX.Web.Helpers;

namespace ObjeX.Tests.Unit;

public class UploadTextTests
{
    private static UploadQueue Queue(int count, long size = 1024 * 1024)
    {
        var queue = new UploadQueue();
        queue.Add("", Enumerable.Range(1, count).Select(i => new UploadFile(i, $"f{i}.bin", size, null)));
        return queue;
    }

    [Fact]
    public void WhileRunning_CountsWhatIsLeft_AndTheBytes()
    {
        var queue = Queue(12);
        queue.Apply([new(1, "done"), new(2, "done"), new(3, "done"), new(4, "progress", Loaded: 512 * 1024), new(5, "failed", Status: 507, Error: "Quota")]);

        Assert.Equal("Uploading 8 files", UploadText.Title(queue));
        Assert.Equal("3 of 12 done · 1 failed · 3.5 MB of 12.0 MB", UploadText.Detail(queue));
    }

    [Fact]
    public void Finished_WithoutErrors()
    {
        var queue = Queue(1);
        queue.Apply([new(1, "done")]);

        Assert.Equal("1 file uploaded", UploadText.Title(queue));
        Assert.Equal("1.0 MB", UploadText.Detail(queue));
    }

    [Fact]
    public void Finished_WithFailuresAndCancels()
    {
        var queue = Queue(12);
        queue.Apply(Enumerable.Range(1, 10).Select(i => new UploadEvent(i, "done")).Append(new(11, "failed")).Append(new(12, "cancelled")));

        Assert.Equal("10 of 12 files uploaded", UploadText.Title(queue));
        Assert.Equal("1 failed · 1 cancelled", UploadText.Detail(queue));
    }

    [Fact]
    public void Tone_TurnsDanger_OnlyWhenARunEndsWithFailures()
    {
        var queue = Queue(2);
        queue.Apply([new(1, "failed")]);
        Assert.Equal(OxTone.Default, UploadText.Tone(queue));

        queue.Apply([new(2, "done")]);
        Assert.Equal(OxTone.Danger, UploadText.Tone(queue));
    }

    [Fact]
    public void RowProgress_OnlyWhileSending()
    {
        var queue = Queue(3, 200);
        queue.Apply([new(1, "progress", Loaded: 50), new(2, "done")]);

        Assert.Equal([25.0, null, null], queue.Items.Select(UploadText.RowProgress));
    }

    [Fact]
    public void LeaveQuestion_CountsTheFilesLeft()
    {
        var queue = Queue(3);
        Assert.Equal("3 files are still uploading. Leave the page and cancel them?", UploadText.LeaveQuestion(queue));

        queue.Apply([new(1, "done"), new(2, "progress", Loaded: 1), new(3, "cancelled")]);
        Assert.Equal("1 file is still uploading. Leave the page and cancel it?", UploadText.LeaveQuestion(queue));
    }

    [Theory]
    [InlineData(0, null, "The connection to the server was lost.")]
    [InlineData(401, null, "Your session has ended. Sign in again.")]
    [InlineData(413, null, "The file is larger than the server accepts.")]
    [InlineData(500, null, "Upload failed (HTTP 500).")]
    [InlineData(507, "Storage quota of the bucket owner exceeded.", "Storage quota of the bucket owner exceeded.")]
    public void ErrorOf_PrefersTheServersMessage(int status, string? message, string expected)
        => Assert.Equal(expected, UploadText.ErrorOf(status, message));

    [Fact]
    public void Row_DescribesEachState()
    {
        var queue = Queue(5, 4 * 1024 * 1024);
        queue.Apply([new(2, "progress", Loaded: 1024 * 1024), new(3, "done"), new(4, "failed", Status: 507, Error: "Quota"), new(5, "cancelled")]);
        var items = queue.Items;

        Assert.Equal(("Waiting · 4.0 MB", OxTone.Muted), (UploadText.Row(items[0]), UploadText.RowTone(items[0])));
        Assert.Equal(("1.0 MB of 4.0 MB", OxTone.Muted), (UploadText.Row(items[1]), UploadText.RowTone(items[1])));
        Assert.Equal(("4.0 MB", OxTone.Accent), (UploadText.Row(items[2]), UploadText.RowTone(items[2])));
        Assert.Equal(("Quota", OxTone.Danger), (UploadText.Row(items[3]), UploadText.RowTone(items[3])));
        Assert.Equal(("Cancelled", OxTone.Muted), (UploadText.Row(items[4]), UploadText.RowTone(items[4])));
    }
}
