using Microsoft.EntityFrameworkCore;
using ObjeX.Infrastructure.Data;

namespace ObjeX.Api.S3;

public static class StorageQuota
{
    public static async Task<IResult?> CheckAsync(ObjeXDbContext db, string userId, long additionalBytes)
    {
        var user = await db.Users.FindAsync(userId);
        if (user is null) return null;

        // Per-user quota (explicitly set) takes priority for any role
        var quota = user.StorageQuotaBytes;

        // Global default only applies to non-privileged users (User role)
        if (quota is null)
        {
            var isPrivileged = await db.UserRoles
                .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
                .AnyAsync(x => x.UserId == userId && (x.Name == "Admin" || x.Name == "Manager"));

            if (isPrivileged) return null; // Admin/Manager unlimited by default

            var settings = await db.SystemSettings.FindAsync(1);
            quota = settings?.DefaultStorageQuotaBytes;
        }

        if (quota is null) return null;

        var currentUsage = await db.Buckets
            .Where(b => b.OwnerId == userId)
            .SumAsync(b => b.TotalSize);

        if (currentUsage + additionalBytes > quota.Value)
            return S3Xml.Error(S3Errors.EntityTooLarge,
                $"Storage quota exceeded ({currentUsage + additionalBytes} bytes requested, {quota.Value} bytes allowed).", 507);

        return null;
    }
}
