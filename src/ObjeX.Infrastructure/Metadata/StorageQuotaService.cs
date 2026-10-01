using Microsoft.EntityFrameworkCore;
using ObjeX.Core.Interfaces;
using ObjeX.Infrastructure.Data;

namespace ObjeX.Infrastructure.Metadata;

public class StorageQuotaService(IDbContextFactory<ObjeXDbContext> dbFactory) : IStorageQuotaService
{
    public async Task<StorageQuotaStatus> GetAsync(string userId, CancellationToken ctk = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ctk);

        var used = await db.Buckets.Where(b => b.OwnerId == userId).SumAsync(b => b.TotalSize, ctk);

        var quota = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.StorageQuotaBytes)
            .FirstOrDefaultAsync(ctk);

        if (quota is null)
        {
            var privileged = await db.UserRoles
                .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
                .AnyAsync(x => x.UserId == userId && (x.Name == "Admin" || x.Name == "Manager"), ctk);

            if (!privileged)
                quota = (await db.SystemSettings.FindAsync([1], ctk))?.DefaultStorageQuotaBytes;
        }

        return new StorageQuotaStatus(used, quota);
    }

    public async Task<QuotaExceeded?> CheckWriteAsync(string bucketName, string key, long newSize, CancellationToken ctk = default)
    {
        string? ownerId;
        long existingSize;
        await using (var db = await dbFactory.CreateDbContextAsync(ctk))
        {
            ownerId = await db.Buckets.Where(b => b.Name == bucketName).Select(b => b.OwnerId).FirstOrDefaultAsync(ctk);
            if (ownerId is null)
                return null;

            existingSize = await db.BlobObjects
                .Where(o => o.BucketName == bucketName && o.Key == key)
                .Select(o => o.Size)
                .FirstOrDefaultAsync(ctk);
        }

        var status = await GetAsync(ownerId, ctk);
        if (status.QuotaBytes is not { } quota)
            return null;

        var requested = status.UsedBytes + Math.Max(0, newSize - existingSize);
        return requested > quota ? new QuotaExceeded(requested, quota) : null;
    }
}
