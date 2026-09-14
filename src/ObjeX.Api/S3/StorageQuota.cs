using System.Security.Claims;
using ObjeX.Core.Interfaces;

namespace ObjeX.Api.S3;

public static class StorageQuota
{
    /// <summary>507 when the caller's quota would be exceeded by <paramref name="additionalBytes"/>, otherwise null.</summary>
    public static async Task<IResult?> CheckAsync(HttpContext ctx, long additionalBytes)
    {
        var userId = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var status = await ctx.RequestServices.GetRequiredService<IStorageQuotaService>().GetAsync(userId, ctx.RequestAborted);

        if (status.QuotaBytes is not { } quota)
            return null;

        if (status.UsedBytes + additionalBytes > quota)
            return S3Xml.Error(S3Errors.EntityTooLarge,
                $"Storage quota exceeded ({status.UsedBytes + additionalBytes} bytes requested, {quota} bytes allowed).", 507);

        return null;
    }
}
