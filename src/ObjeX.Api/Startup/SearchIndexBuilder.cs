using System.Diagnostics;

using Microsoft.EntityFrameworkCore;

using ObjeX.Infrastructure.Data;

namespace ObjeX.Api.Startup;

/// <summary>
/// Builds the trigram index that object search uses on PostgreSQL, after the host has started. On millions of keys the
/// build outlasts the command timeout of a migration and a health probe's patience, so it runs here without a timeout;
/// CONCURRENTLY keeps uploads and deletes working meanwhile. Registered for PostgreSQL only.
/// </summary>
public sealed class SearchIndexBuilder(IDbContextFactory<ObjeXDbContext> contexts, ILogger<SearchIndexBuilder> logger) : BackgroundService
{
    public const string IndexName = "ix_blob_objects_key_trgm";

    const string HasExtensionSql = "SELECT EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'pg_trgm') AS \"Value\"";
    const string IndexValidSql =
        "SELECT i.indisvalid AS \"Value\" FROM pg_index i JOIN pg_class c ON c.oid = i.indexrelid " +
        "JOIN pg_namespace n ON n.oid = c.relnamespace WHERE c.relname = '" + IndexName + "' AND n.nspname = current_schema()";
    const string DropSql = "DROP INDEX CONCURRENTLY IF EXISTS " + IndexName;
    const string CreateSql = "CREATE INDEX CONCURRENTLY IF NOT EXISTS " + IndexName + " ON blob_objects USING gin (lower(key) gin_trgm_ops)";

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
            logger.LogWarning(ex, "Object search runs without its trigram index: the build failed. ObjeX tries again on its next start.");
        }
    }

    /// <summary>
    /// Creates the index when pg_trgm is installed and the index is missing. A build that was interrupted (a restart, a
    /// cancelled command) leaves an invalid index that IF NOT EXISTS would skip, so that one is dropped and built again.
    /// </summary>
    public async Task EnsureAsync(CancellationToken ctk = default)
    {
        await _gate.WaitAsync(ctk);
        try
        {
            await using var db = await contexts.CreateDbContextAsync(ctk);
            db.Database.SetCommandTimeout(0);

            if (!await db.Database.SqlQueryRaw<bool>(HasExtensionSql).SingleAsync(ctk))
            {
                logger.LogWarning("Object search runs without its trigram index: the pg_trgm extension is missing. " +
                    "As a database administrator, run CREATE EXTENSION IF NOT EXISTS pg_trgm; ObjeX builds the index on its next start.");
                return;
            }

            var valid = await db.Database.SqlQueryRaw<bool>(IndexValidSql).ToListAsync(ctk);
            if (valid is [true]) return;
            if (valid is [false])
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
}
