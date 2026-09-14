using System.Diagnostics;
using System.Security.Cryptography;

using Microsoft.Extensions.Logging;

using ObjeX.Core.Interfaces;
using ObjeX.Infrastructure.Storage;

namespace ObjeX.Infrastructure.Jobs;

public record IntegrityResult(int Checked, int Corrupted, int Missing, double DurationSeconds, DateTime Timestamp);

public class VerifyBlobIntegrityJob(
    IMetadataService metadataService,
    FileSystemStorageService storageService,
    ILogger<VerifyBlobIntegrityJob> logger)
{
    public async Task<IntegrityResult> ExecuteAsync()
    {
        logger.LogInformation("Blob integrity verification started");
        var sw = Stopwatch.StartNew();

        var allObjects = await metadataService.ListAllObjectsAsync();
        var checked_ = 0;
        var corrupted = 0;
        var missing = 0;

        foreach (var obj in allObjects)
        {
            if (string.IsNullOrEmpty(obj.ETag))
                continue;

            var path = storageService.GetFilePath(obj.BucketName, obj.Key);
            if (!File.Exists(path))
            {
                missing++;
                logger.LogError("Blob missing for {Bucket}/{Key} — expected at {Path}", obj.BucketName, obj.Key, path);
                continue;
            }

            checked_++;
            var actualETag = await ComputeMd5Async(path);
            if (!string.Equals(actualETag, obj.ETag, StringComparison.OrdinalIgnoreCase))
            {
                corrupted++;
                logger.LogError("Blob integrity failure for {Bucket}/{Key} — stored ETag {Stored}, actual {Actual}", obj.BucketName, obj.Key, obj.ETag, actualETag);
            }
        }

        sw.Stop();
        var result = new IntegrityResult(checked_, corrupted, missing, sw.Elapsed.TotalSeconds, DateTime.UtcNow);

        if (corrupted > 0 || missing > 0)
            logger.LogWarning("Blob integrity check finished — checked {Checked}, corrupted {Corrupted}, missing {Missing}, duration {Duration:F2}s",
                result.Checked, result.Corrupted, result.Missing, result.DurationSeconds);
        else
            logger.LogInformation("Blob integrity check finished — all {Checked} blobs verified OK, duration {Duration:F2}s",
                result.Checked, result.DurationSeconds);

        return result;
    }

    private static async Task<string> ComputeMd5Async(string filePath)
    {
        await using var stream = File.OpenRead(filePath);
        var hash = await MD5.HashDataAsync(stream);
        return Convert.ToHexString(hash).ToLower();
    }
}
