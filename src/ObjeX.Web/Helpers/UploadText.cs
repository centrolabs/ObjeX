using ObjeX.Web.Components.Ui;

namespace ObjeX.Web.Helpers;

/// <summary>How the upload panel puts the queue in words: its title and line, one line per file, the error of a failed request.</summary>
public static class UploadText
{
    public static string Title(UploadQueue queue)
    {
        var done = queue.CountOf(UploadStatus.Done);
        if (queue.IsActive)
            return $"Uploading {Files(queue.CountOf(UploadStatus.Queued) + queue.CountOf(UploadStatus.Uploading))}";
        return done == queue.Items.Count ? $"{Files(done)} uploaded" : $"{done} of {Files(queue.Items.Count)} uploaded";
    }

    public static string Detail(UploadQueue queue)
    {
        var parts = new List<string>();
        if (queue.IsActive)
            parts.Add($"{queue.CountOf(UploadStatus.Done)} of {queue.Items.Count} done");
        if (queue.CountOf(UploadStatus.Failed) is > 0 and var failed)
            parts.Add($"{failed} failed");
        if (queue.CountOf(UploadStatus.Cancelled) is > 0 and var cancelled)
            parts.Add($"{cancelled} cancelled");
        if (queue.IsActive)
            parts.Add($"{FileHelper.FormatBytes(queue.SentBytes)} of {FileHelper.FormatBytes(queue.TotalBytes)}");
        else if (parts.Count == 0)
            parts.Add(FileHelper.FormatBytes(queue.TotalBytes));
        return string.Join(" · ", parts);
    }

    /// <summary>The bar turns red once a run has ended with files that did not go up.</summary>
    public static OxTone Tone(UploadQueue queue) =>
        !queue.IsActive && queue.CountOf(UploadStatus.Failed) > 0 ? OxTone.Danger : OxTone.Default;

    public static string Row(UploadItem item) => item.Status switch
    {
        UploadStatus.Queued => $"Waiting · {FileHelper.FormatBytes(item.Size)}",
        UploadStatus.Uploading => $"{FileHelper.FormatBytes(item.Loaded)} of {FileHelper.FormatBytes(item.Size)}",
        UploadStatus.Done => FileHelper.FormatBytes(item.Size),
        UploadStatus.Failed => item.Error ?? "Failed",
        _ => "Cancelled",
    };

    /// <summary>The bar of a file while it is sent, null otherwise.</summary>
    public static double? RowProgress(UploadItem item) =>
        item.Status != UploadStatus.Uploading ? null : item.Size > 0 ? 100.0 * item.Loaded / item.Size : 0;

    public static string LeaveQuestion(UploadQueue queue)
    {
        var left = queue.CountOf(UploadStatus.Queued) + queue.CountOf(UploadStatus.Uploading);
        return $"{Files(left)} {(left == 1 ? "is" : "are")} still uploading. Leave the page and cancel {(left == 1 ? "it" : "them")}?";
    }

    public static OxTone RowTone(UploadItem item) => item.Status switch
    {
        UploadStatus.Done => OxTone.Accent,
        UploadStatus.Failed => OxTone.Danger,
        _ => OxTone.Muted,
    };

    /// <summary>The server's own message when it sent one; status 0 means the request got no answer at all.</summary>
    public static string ErrorOf(int status, string? serverMessage) => serverMessage ?? status switch
    {
        0 => "The connection to the server was lost.",
        401 => "Your session has ended. Sign in again.",
        413 => "The file is larger than the server accepts.",
        _ => $"Upload failed (HTTP {status}).",
    };

    private static string Files(int count) => count == 1 ? "1 file" : $"{count} files";
}
