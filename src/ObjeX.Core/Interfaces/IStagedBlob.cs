namespace ObjeX.Core.Interfaces;

/// <summary>
/// A blob written to a temporary location. The object under the key keeps its old bytes
/// until <see cref="CommitAsync"/> runs, so a failed check can still return without damage.
/// Disposing without a commit deletes the temporary file.
/// </summary>
public interface IStagedBlob : IAsyncDisposable
{
    long Size { get; }
    Task<string> CommitAsync(CancellationToken ctk = default);
}
