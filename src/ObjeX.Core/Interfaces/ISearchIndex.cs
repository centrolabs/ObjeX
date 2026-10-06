namespace ObjeX.Core.Interfaces;

public enum SearchIndexState
{
    /// <summary>SQLite, which searches without an index.</summary>
    NotUsed,
    /// <summary>Switched off by configuration, and no index left.</summary>
    Off,
    /// <summary>Switched off by configuration; the index still exists until the drop at start is done.</summary>
    Dropping,
    /// <summary>The PostgreSQL extension pg_trgm is not installed.</summary>
    ExtensionMissing,
    Building,
    Ready,
    /// <summary>Not built yet, or a build failed or was interrupted; the next start builds it.</summary>
    Missing,
}

public record SearchIndexStatus(SearchIndexState State, long? SizeBytes = null);

/// <summary>The index behind object search, for the Settings page: read only, the configuration decides.</summary>
public interface ISearchIndex
{
    Task<SearchIndexStatus> GetStatusAsync(CancellationToken ctk = default);
}
