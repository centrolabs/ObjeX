using ObjeX.Core.Interfaces;

namespace ObjeX.Api.S3;

public static class ObjectDeletion
{
    /// <summary>Row first, blob second: a row must never point at a missing blob, while a leftover blob is collected by the orphan cleanup job.</summary>
    public static async Task DeleteAsync(HttpContext ctx, IMetadataService metadata, IObjectStorageService storage, string bucket, string key, string? auditUserId)
    {
        await metadata.DeleteObjectAsync(bucket, key, auditUserId, ctx.RequestAborted);
        await DeleteBlobAsync(ctx, storage, bucket, key);
    }

    /// <summary>Same order as <see cref="DeleteAsync"/>, but the rows go in one transaction and one stats update; the blobs stay per file.</summary>
    public static async Task<int> DeleteManyAsync(HttpContext ctx, IMetadataService metadata, IObjectStorageService storage, string bucket, IReadOnlyCollection<string> keys, string? auditUserId)
    {
        var deleted = await metadata.DeleteObjectsAsync(bucket, keys, auditUserId, ctx.RequestAborted);
        foreach (var key in keys)
            await DeleteBlobAsync(ctx, storage, bucket, key);
        return deleted;
    }

    private static async Task DeleteBlobAsync(HttpContext ctx, IObjectStorageService storage, string bucket, string key)
    {
        try
        {
            await storage.DeleteAsync(bucket, key, ctx.RequestAborted);
        }
        catch (Exception ex)
        {
            ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(ObjectDeletion))
                .LogWarning(ex, "Blob {Bucket}/{Key} could not be deleted and is left for the orphan cleanup job", bucket, key);
        }
    }
}
