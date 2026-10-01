using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ObjeX.Core.Interfaces;
using ObjeX.Infrastructure.Data;
using ObjeX.Infrastructure.Storage;

namespace ObjeX.Infrastructure.Jobs;

public record CleanupResult(int FilesChecked, int FilesDeleted, double DurationSeconds, DateTime Timestamp);

public class CleanupOrphanedBlobsJob(
    IMetadataService metadataService,
    FileSystemStorageService storageService,
    IDbContextFactory<ObjeXDbContext> dbFactory,
    ILogger<CleanupOrphanedBlobsJob> logger)
{
    // A blob is written before its row; anything this recent may belong to an upload still in flight.
    public const int DefaultGraceMinutes = 60;

    public async Task<CleanupResult> ExecuteAsync()
    {
        logger.LogInformation("Orphaned blob cleanup started");
        var sw = Stopwatch.StartNew();
        var cutoff = DateTime.UtcNow - TimeSpan.FromMinutes(await GraceMinutesAsync());

        // Known paths are derived from bucket and key, so a moved data directory never turns every blob into an orphan.
        var allObjects = await metadataService.ListAllObjectsAsync();
        var knownPaths = new HashSet<string>(
            allObjects.Select(o => Path.GetFullPath(storageService.GetFilePath(o.BucketName, o.Key))),
            StringComparer.Ordinal);

        var files = await Task.Run(() =>
            Directory.EnumerateFiles(storageService.BasePath, "*.blob", SearchOption.AllDirectories).ToList());

        var deleted = 0;
        foreach (var file in files)
        {
            if (knownPaths.Contains(Path.GetFullPath(file)) || File.GetLastWriteTimeUtc(file) >= cutoff)
                continue;

            File.Delete(file);
            deleted++;
        }

        sw.Stop();
        var result = new CleanupResult(files.Count, deleted, sw.Elapsed.TotalSeconds, DateTime.UtcNow);

        logger.LogInformation(
            "Orphaned blob cleanup finished — checked {FilesChecked}, deleted {FilesDeleted}, duration {DurationSeconds:F2}s",
            result.FilesChecked, result.FilesDeleted, result.DurationSeconds);

        return result;
    }

    private async Task<int> GraceMinutesAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.SystemSettings.Select(s => s.OrphanGraceMinutes).SingleAsync() ?? DefaultGraceMinutes;
    }
}
