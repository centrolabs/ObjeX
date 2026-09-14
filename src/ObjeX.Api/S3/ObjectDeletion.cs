using ObjeX.Core.Interfaces;

namespace ObjeX.Api.S3;

public static class ObjectDeletion
{
    /// <summary>Row first, blob second: a row must never point at a missing blob, while a leftover blob is collected by the orphan cleanup job.</summary>
    public static async Task DeleteAsync(HttpContext ctx, IMetadataService metadata, IObjectStorageService storage, string bucket, string key, string? auditUserId)
    {
        await metadata.DeleteObjectAsync(bucket, key, auditUserId, ctx.RequestAborted);
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
