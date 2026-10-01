using ObjeX.Core.Interfaces;

namespace ObjeX.Api.S3;

public static class StorageQuota
{
    /// <summary>507 when the bucket owner's quota refuses the write (see <see cref="IStorageQuotaService.CheckWriteAsync"/>), otherwise null.</summary>
    public static async Task<IResult?> CheckAsync(HttpContext ctx, string bucketName, string key, long newSize)
    {
        var exceeded = await ctx.RequestServices.GetRequiredService<IStorageQuotaService>()
            .CheckWriteAsync(bucketName, key, newSize, ctx.RequestAborted);

        return exceeded is null
            ? null
            : S3Xml.Error(S3Errors.EntityTooLarge,
                $"Storage quota exceeded ({exceeded.RequestedBytes} bytes requested, {exceeded.QuotaBytes} bytes allowed).", 507);
    }
}
