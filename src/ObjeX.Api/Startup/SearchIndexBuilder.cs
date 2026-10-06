using System.Diagnostics;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using ObjeX.Api.Options;
using ObjeX.Core.Interfaces;
using ObjeX.Infrastructure.Data;

namespace ObjeX.Api.Startup;

/// <summary>
/// Builds the trigram index that object search uses on PostgreSQL, after the host has started. On millions of keys the
/// build outlasts the command timeout of a migration and a health probe's patience, so it runs here without a timeout;
/// CONCURRENTLY keeps uploads and deletes working meanwhile. Search:TrigramIndex false drops the index instead.
/// Registered for PostgreSQL only.
/// </summary>
public sealed class SearchIndexBuilder(
    IDbContextFactory<ObjeXDbContext> contexts, IOptions<SearchOptions> options, ILogger<SearchIndexBuilder> logger) : BackgroundService, ISearchIndex
{
    public const string IndexName = "ix_blob_objects_key_trgm";

    const string HasExtensionSql = "SELECT EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'pg_trgm') AS \"Value\"";
    const string IndexFilter =
        "FROM pg_index i JOIN pg_class c ON c.oid = i.indexrelid JOIN pg_namespace n ON n.oid = c.relnamespace " +
        "WHERE c.relname = '" + IndexName + "' AND n.nspname = current_schema()";
    // One row per index: its size, or -1 when an interrupted build left it invalid.
    const string IndexSql = "SELECT CASE WHEN i.indisvalid THEN pg_relation_size(c.oid) ELSE -1 END AS \"Value\" " + IndexFilter;
    const string DropSql = "DROP INDEX CONCURRENTLY IF EXISTS " + IndexName;
    const string CreateSql = "CREATE INDEX CONCURRENTLY IF NOT EXISTS " + IndexName + " ON blob_objects USING gin (lower(key) gin_trgm_ops)";

    // Held while EnsureAsync runs, so a held gate is a build or a drop in progress.
    readonly SemaphoreSlim _gate = new(1, 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        try
        {
            await EnsureAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            logger.LogWarning(ex, options.Value.TrigramIndex
                ? "Object search runs without its trigram index: the build failed. ObjeX tries again on its next start."
                : "Dropping the trigram index for object search failed. ObjeX tries again on its next start.");
        }
    }

    /// <summary>
    /// Creates the index when pg_trgm is installed and the index is missing. A build that was interrupted (a restart, a
    /// cancelled command) leaves an invalid index that IF NOT EXISTS would skip, so that one is dropped and built again.
    /// With Search:TrigramIndex false it drops the index.
    /// </summary>
    public async Task EnsureAsync(CancellationToken ctk = default)
    {
        await _gate.WaitAsync(ctk);
        try
        {
            await using var db = await contexts.CreateDbContextAsync(ctk);
            db.Database.SetCommandTimeout(0);
            var index = await db.Database.SqlQueryRaw<long>(IndexSql).ToListAsync(ctk);

            if (!options.Value.TrigramIndex)
            {
                if (index.Count == 0) return;
                await db.Database.ExecuteSqlRawAsync(DropSql, ctk);
                logger.LogInformation("Dropped the trigram index for object search: Search:TrigramIndex is false.");
                return;
            }

            if (!await db.Database.SqlQueryRaw<bool>(HasExtensionSql).SingleAsync(ctk))
            {
                logger.LogWarning("Object search runs without its trigram index: the pg_trgm extension is missing. " +
                    "As a database administrator, run CREATE EXTENSION IF NOT EXISTS pg_trgm; ObjeX builds the index on its next start.");
                return;
            }

            if (index is [>= 0]) return;
            if (index is [< 0])
                await db.Database.ExecuteSqlRawAsync(DropSql, ctk);

            logger.LogInformation("Building the trigram index for object search; uploads and deletes keep working meanwhile.");
            var started = Stopwatch.GetTimestamp();
            await db.Database.ExecuteSqlRawAsync(CreateSql, ctk);
            logger.LogInformation("Built the trigram index for object search in {Seconds:F1} s.", Stopwatch.GetElapsedTime(started).TotalSeconds);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SearchIndexStatus> GetStatusAsync(CancellationToken ctk = default)
    {
        var on = options.Value.TrigramIndex;
        if (_gate.CurrentCount == 0) return new(on ? SearchIndexState.Building : SearchIndexState.Dropping);

        await using var db = await contexts.CreateDbContextAsync(ctk);
        var index = await db.Database.SqlQueryRaw<long>(IndexSql).ToListAsync(ctk);
        if (!on) return index is [var left] ? new(SearchIndexState.Dropping, left >= 0 ? left : null) : new(SearchIndexState.Off);
        if (!await db.Database.SqlQueryRaw<bool>(HasExtensionSql).SingleAsync(ctk)) return new(SearchIndexState.ExtensionMissing);

        return index is [>= 0 and var size] ? new(SearchIndexState.Ready, size) : new(SearchIndexState.Missing);
    }
}
