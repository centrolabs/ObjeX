using ObjeX.Core.Interfaces;

namespace ObjeX.Api.Startup;

/// <summary>SQLite has no search index; a scan takes about 100 ms per million keys.</summary>
public sealed class NoSearchIndex : ISearchIndex
{
    public Task<SearchIndexStatus> GetStatusAsync(CancellationToken ctk = default) => Task.FromResult(new SearchIndexStatus(SearchIndexState.NotUsed));
}
