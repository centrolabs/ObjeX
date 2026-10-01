namespace ObjeX.Core.Interfaces;

/// <summary>
/// One writer per key at a time within the process: an upload moves its file and writes its row, a delete removes row and file, and
/// neither may interleave with another on the same key. Several keys are entered in ordinal order, so two callers never wait in a circle.
/// </summary>
public interface IKeyGate
{
    Task<IDisposable> EnterAsync(string bucketName, IEnumerable<string> keys, CancellationToken ctk = default);
}
