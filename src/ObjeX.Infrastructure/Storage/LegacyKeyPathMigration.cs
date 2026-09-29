using System.Security.Cryptography;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using ObjeX.Core.Models;
using ObjeX.Core.Utilities;
using ObjeX.Infrastructure.Data;

namespace ObjeX.Infrastructure.Storage;

/// <summary>
/// Up to 1.2.4 the blob path hashed the key after stripping ".." and turning "\" into "/", so aliases
/// like "x..y" and "xy" shared one file. Moves each alias blob to the path of its raw key. Where
/// several keys claim one file, the stored ETag decides the owner; the others are logged as lost.
/// </summary>
public class LegacyKeyPathMigration(ObjeXDbContext db, FileSystemStorageService storage, ILogger<LegacyKeyPathMigration> logger)
{
    public async Task<int> RunAsync(CancellationToken ctk = default)
    {
        var aliases = await db.BlobObjects.AsNoTracking()
            .Where(o => o.Key.Contains("..") || o.Key.Contains("\\"))
            .ToListAsync(ctk);

        var moved = 0;
        foreach (var group in aliases.GroupBy(o => (o.BucketName, LegacyKey: LegacyKey(o.Key))))
        {
            var pending = group.Where(o => !File.Exists(storage.GetFilePath(o.BucketName, o.Key))).ToList();
            if (pending.Count == 0)
                continue;

            var legacyPath = storage.GetFilePath(group.Key.BucketName, group.Key.LegacyKey);
            if (!File.Exists(legacyPath))
            {
                LogLost(pending);
                continue;
            }

            var owner = await db.BlobObjects.AsNoTracking()
                .FirstOrDefaultAsync(o => o.BucketName == group.Key.BucketName && o.Key == group.Key.LegacyKey, ctk);
            List<BlobObject> claimants = owner is null ? pending : [owner, .. pending];

            var winner = claimants.Count == 1 ? claimants[0] : await MatchByETagAsync(legacyPath, claimants, ctk);
            if (winner is null)
            {
                logger.LogWarning("Blob {Path} matches none of the keys {Keys} in bucket {Bucket}; left in place",
                    legacyPath, claimants.Select(o => o.Key), group.Key.BucketName);
                continue;
            }

            if (winner != owner)
            {
                var target = storage.GetFilePath(winner.BucketName, winner.Key);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Move(legacyPath, target);
                moved++;
            }

            LogLost(claimants.Where(o => o != winner));
        }

        if (moved > 0)
            logger.LogInformation("Moved {Count} blob(s) to the path of their raw key", moved);
        return moved;
    }

    private static string LegacyKey(string key) => key.Replace("..", "").Replace('\\', '/');

    private static async Task<BlobObject?> MatchByETagAsync(string path, List<BlobObject> claimants, CancellationToken ctk)
    {
        await using var stream = File.OpenRead(path);
        var md5 = Convert.ToHexStringLower(await MD5.HashDataAsync(stream, ctk));
        return claimants.FirstOrDefault(o => !ETags.IsMultipart(o.ETag) && string.Equals(o.ETag, md5, StringComparison.OrdinalIgnoreCase));
    }

    private void LogLost(IEnumerable<BlobObject> objects)
    {
        foreach (var o in objects)
            logger.LogWarning("Blob for {Bucket}/{Key} was overwritten by a colliding key before 1.2.5; re-upload it", o.BucketName, o.Key);
    }
}
