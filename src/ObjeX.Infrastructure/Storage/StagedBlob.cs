using ObjeX.Core.Interfaces;

namespace ObjeX.Infrastructure.Storage;

public class StagedBlob(string tmpPath, string finalPath, long size) : IStagedBlob
{
    private bool _committed;

    public long Size => size;

    public Task<string> CommitAsync(CancellationToken ctk = default)
    {
        ctk.ThrowIfCancellationRequested();
        File.Move(tmpPath, finalPath, overwrite: true);
        _committed = true;
        return Task.FromResult(finalPath);
    }

    public ValueTask DisposeAsync()
    {
        if (!_committed)
            File.Delete(tmpPath);
        return ValueTask.CompletedTask;
    }
}

public sealed class StagedPart(string tmpPath, string partPath, long size, string etag)
    : StagedBlob(tmpPath, partPath, size)
{
    public string ETag => etag;
}
