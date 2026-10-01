using ObjeX.Core.Interfaces;

namespace ObjeX.Api.S3;

public static class StorageQuota
{
    /// <summary>507 when the bucket owner's quota refuses the write (see <see cref="IStorageQuotaService.CheckWriteAsync"/>), otherwise null.</summary>
    public static async Task<IResult?> CheckAsync(HttpContext ctx, string bucketName, string key, long newSize)
    {
        var exceeded = await ctx.RequestServices.GetRequiredService<IStorageQuotaService>()
            .CheckWriteAsync(bucketName, key, newSize, ctx.RequestAborted);
        return exceeded is null ? null : Error(exceeded);
    }

    /// <summary>Runs write, the commit and the row, behind the owner's quota gate (see <see cref="IStorageQuotaService.WriteWithinQuotaAsync"/>); 507 when refused.</summary>
    public static async Task<IResult?> WriteAsync(HttpContext ctx, string bucketName, string key, long newSize, Func<Task> write)
    {
        var exceeded = await ctx.RequestServices.GetRequiredService<IStorageQuotaService>()
            .WriteWithinQuotaAsync(bucketName, key, newSize, write, ctx.RequestAborted);
        return exceeded is null ? null : Error(exceeded);
    }

    private static IResult Error(QuotaExceeded exceeded) =>
        S3Xml.Error(S3Errors.EntityTooLarge,
            $"Storage quota exceeded ({exceeded.RequestedBytes} bytes requested, {exceeded.QuotaBytes} bytes allowed).", 507);
}
