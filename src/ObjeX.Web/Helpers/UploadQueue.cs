using ObjeX.Core.Validation;

namespace ObjeX.Web.Helpers;

public enum UploadStatus { Queued, Uploading, Done, Failed, Cancelled }

/// <summary>A file the upload script collected, under the id it gave the file. Path is relative to the drop or the picked folder.</summary>
public record UploadFile(int Id, string Path, long Size, string? Type);

/// <summary>A report of the upload script: "progress" with the bytes sent, "done", "failed" with the HTTP status (0 = no answer) and the server's message, or "cancelled".</summary>
public record UploadEvent(int Id, string Kind, long Loaded = 0, int Status = 0, string? Error = null);

public sealed class UploadItem
{
    public required int Id { get; init; }
    public required string Key { get; init; }
    /// <summary>The path as dropped or picked, shown in the list.</summary>
    public required string Name { get; init; }
    public required long Size { get; init; }
    public required string ContentType { get; init; }
    /// <summary>False when the key itself is refused: sending the file again would fail the same way.</summary>
    public required bool Sendable { get; init; }
    public UploadStatus Status { get; internal set; }
    public long Loaded { get; internal set; }
    public string? Error { get; internal set; }

    public bool CanRetry => Sendable && Status is UploadStatus.Failed or UploadStatus.Cancelled;
}

/// <summary>The uploads of the Objects page: the keys they go to, their state and the totals the panel shows.</summary>
public sealed class UploadQueue
{
    private readonly List<UploadItem> _items = [];
    private readonly Dictionary<int, UploadItem> _byId = [];

    public IReadOnlyList<UploadItem> Items => _items;

    public static string KeyFor(string prefix, string path) => prefix + path.TrimStart('/');

    /// <summary>Queues the files under the prefix and returns those to send; a key the validator refuses fails at once.</summary>
    public IReadOnlyList<UploadItem> Add(string prefix, IEnumerable<UploadFile> files)
    {
        var toSend = new List<UploadItem>();
        foreach (var file in files)
        {
            var key = KeyFor(prefix, file.Path);
            var keyError = ObjectKeyValidator.GetValidationError(key);
            var item = new UploadItem
            {
                Id = file.Id,
                Key = key,
                Name = file.Path.TrimStart('/'),
                Size = file.Size,
                ContentType = string.IsNullOrEmpty(file.Type) ? "application/octet-stream" : file.Type,
                Sendable = keyError is null,
                Status = keyError is null ? UploadStatus.Queued : UploadStatus.Failed,
                Error = keyError
            };
            _items.Add(item);
            _byId[item.Id] = item;
            if (item.Sendable)
                toSend.Add(item);
        }
        return toSend;
    }

    public void Apply(IEnumerable<UploadEvent> events)
    {
        foreach (var e in events)
        {
            if (!_byId.TryGetValue(e.Id, out var item))
                continue;

            switch (e.Kind)
            {
                case "progress" when item.Status is UploadStatus.Queued or UploadStatus.Uploading:
                    item.Status = UploadStatus.Uploading;
                    item.Loaded = e.Loaded;
                    break;
                case "done":
                    (item.Status, item.Loaded, item.Error) = (UploadStatus.Done, item.Size, null);
                    break;
                case "failed":
                    (item.Status, item.Loaded, item.Error) = (UploadStatus.Failed, 0, UploadText.ErrorOf(e.Status, e.Error));
                    break;
                case "cancelled":
                    (item.Status, item.Loaded, item.Error) = (UploadStatus.Cancelled, 0, null);
                    break;
            }
        }
    }

    /// <summary>Puts failed and cancelled files back in the queue, the one with the id or all of them, and returns those to send.</summary>
    public IReadOnlyList<UploadItem> Retry(int? id = null)
    {
        var retried = _items.Where(i => i.CanRetry && (id is null || i.Id == id)).ToList();
        foreach (var item in retried)
            (item.Status, item.Loaded, item.Error) = (UploadStatus.Queued, 0, null);
        return retried;
    }

    /// <summary>Empties the list and returns the ids, so the script lets go of the files.</summary>
    public IReadOnlyList<int> Clear()
    {
        var ids = _items.Select(i => i.Id).ToList();
        _items.Clear();
        _byId.Clear();
        return ids;
    }

    public bool IsActive => _items.Any(i => i.Status is UploadStatus.Queued or UploadStatus.Uploading);

    public int CountOf(UploadStatus status) => _items.Count(i => i.Status == status);

    /// <summary>Cancelled files leave the totals; failed ones stay, they still have to go up.</summary>
    public long TotalBytes => _items.Where(i => i.Status != UploadStatus.Cancelled).Sum(i => i.Size);

    public long SentBytes => _items.Where(i => i.Status != UploadStatus.Cancelled).Sum(i => i.Loaded);

    public double Percent
    {
        get
        {
            if (TotalBytes > 0)
                return (double)SentBytes / TotalBytes * 100;
            var counted = _items.Count(i => i.Status != UploadStatus.Cancelled);
            return counted == 0 ? 0 : (double)CountOf(UploadStatus.Done) / counted * 100;
        }
    }
}
