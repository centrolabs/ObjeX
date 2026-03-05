using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ObjeX.Core.Interfaces;
using ObjeX.Infrastructure.Storage;

namespace ObjeX.Infrastructure.Jobs;

public record CleanupResult(int FilesChecked, int FilesDeleted, double DurationSeconds, DateTime Timestamp);

public class CleanupOrphanedBlobsJob(
    IMetadataService metadataService,
    FileSystemStorageService storageService,
    ILogger<CleanupOrphanedBlobsJob> logger)
{
    public async Task<CleanupResult> ExecuteAsync()
    {
        logger.LogInformation("Orphaned blob cleanup started");
        var sw = Stopwatch.StartNew();

        var allObjects = await metadataService.ListAllObjectsAsync();
        var knownPaths = new HashSet<string>(allObjects.Select(o => o.StoragePath).OfType<string>());

        var files = await Task.Run(() =>
            Directory.EnumerateFiles(storageService.BasePath, "*.blob", SearchOption.AllDirectories).ToList());

        var deleted = 0;
        foreach (var file in files)
        {
            if (!knownPaths.Contains(file))
            {
                File.Delete(file);
                deleted++;
            }
        }

        sw.Stop();
        var result = new CleanupResult(files.Count, deleted, sw.Elapsed.TotalSeconds, DateTime.UtcNow);

        logger.LogInformation(
            "Orphaned blob cleanup finished — checked {FilesChecked}, deleted {FilesDeleted}, duration {DurationSeconds:F2}s",
            result.FilesChecked, result.FilesDeleted, result.DurationSeconds);

        return result;
    }
}
