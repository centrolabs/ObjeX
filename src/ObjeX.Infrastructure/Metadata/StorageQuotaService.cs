using Microsoft.EntityFrameworkCore;
using ObjeX.Core.Interfaces;
using ObjeX.Infrastructure.Data;

namespace ObjeX.Infrastructure.Metadata;

/// <summary>A singleton: the gates of <see cref="WriteWithinQuotaAsync"/> must be the same for every request of the process.</summary>
public class StorageQuotaService(IDbContextFactory<ObjeXDbContext> dbFactory, IKeyGate keyGate) : IStorageQuotaService
{
    private readonly NamedGates _ownerGates = new();

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
        if (await OwnerOfAsync(bucketName, ctk) is not { } ownerId)
            return null;

        long existingSize;
        await using (var db = await dbFactory.CreateDbContextAsync(ctk))
            existingSize = await db.BlobObjects
                .Where(o => o.BucketName == bucketName && o.Key == key)
                .Select(o => o.Size)
                .FirstOrDefaultAsync(ctk);

        var status = await GetAsync(ownerId, ctk);
        if (status.QuotaBytes is not { } quota)
            return null;

        var requested = status.UsedBytes + Math.Max(0, newSize - existingSize);
        return requested > quota ? new QuotaExceeded(requested, quota) : null;
    }

    public async Task<QuotaExceeded?> WriteWithinQuotaAsync(string bucketName, string key, long newSize, Func<Task> write, CancellationToken ctk = default)
    {
        var limitedOwner = await OwnerOfAsync(bucketName, ctk) is { } ownerId && (await GetAsync(ownerId, ctk)).QuotaBytes is not null ? ownerId : null;

        // Owner gate first, key gate second, in every call, so two calls never wait for each other in a circle.
        // The write ends with the row and the owner's new TotalSize, so the next check behind the owner gate counts it.
        using var ownerGate = limitedOwner is null ? null : await _ownerGates.EnterAsync(limitedOwner, ctk);
        using var keyEntry = await keyGate.EnterAsync(bucketName, [key], ctk);
        if (limitedOwner is not null && await CheckWriteAsync(bucketName, key, newSize, ctk) is { } exceeded)
            return exceeded;
        await write();
        return null;
    }

    private async Task<string?> OwnerOfAsync(string bucketName, CancellationToken ctk)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ctk);
        return await db.Buckets.Where(b => b.Name == bucketName).Select(b => b.OwnerId).FirstOrDefaultAsync(ctk);
    }
}
