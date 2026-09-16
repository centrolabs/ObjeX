using ObjeX.Core.Interfaces;

namespace ObjeX.Api.S3;

public static class StorageQuota
{
    /// <summary>
    /// 507 when the bucket owner's quota would be exceeded, otherwise null. The owner pays for the
    /// bucket whoever uploads, and an overwrite is charged only the growth over the stored size.
    /// </summary>
    public static async Task<IResult?> CheckAsync(HttpContext ctx, string bucketName, string key, long newSize)
    {
        var metadata = ctx.RequestServices.GetRequiredService<IMetadataService>();

        var bucket = await metadata.GetBucketAsync(bucketName, null, ctx.RequestAborted);
        if (bucket is null)
            return null;

        var existingSize = (await metadata.GetObjectAsync(bucketName, key, ctx.RequestAborted))?.Size ?? 0;
        var charged = Math.Max(0, newSize - existingSize);

        var status = await ctx.RequestServices.GetRequiredService<IStorageQuotaService>()
            .GetAsync(bucket.OwnerId, ctx.RequestAborted);

        if (status.QuotaBytes is not { } quota)
            return null;

        if (status.UsedBytes + charged > quota)
            return S3Xml.Error(S3Errors.EntityTooLarge,
                $"Storage quota exceeded ({status.UsedBytes + charged} bytes requested, {quota} bytes allowed).", 507);

        return null;
    }
}
