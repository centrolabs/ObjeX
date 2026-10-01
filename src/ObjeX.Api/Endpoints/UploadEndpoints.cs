using System.Security.Claims;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.Options;

using ObjeX.Core.Interfaces;
using ObjeX.Core.Models;
using ObjeX.Core.Utilities;
using ObjeX.Core.Validation;
using ObjeX.Web.Helpers;

namespace ObjeX.Api.Endpoints;

public static class UploadEndpoints
{
    static string GetCallerId(HttpContext ctx) =>
        ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    static bool IsPrivileged(HttpContext ctx) =>
        ctx.User.IsInRole("Admin") || ctx.User.IsInRole("Manager");

    static IResult Error(int statusCode, string message) =>
        Results.Json(new { error = message }, statusCode: statusCode);

    public static void MapUploadEndpoints(this WebApplication app)
    {
        // Browser upload from the Objects page: one file per request, raw body, same rules as PUT Object on the S3 port.
        app.MapPut("/api/upload/{bucketName}/{*key}", async (
            string bucketName, string key,
            HttpContext ctx, IAntiforgery antiforgery, IOptions<AntiforgeryOptions> antiforgeryOptions,
            IMetadataService metadata, IObjectStorageService storage,
            IStorageQuotaService quota, IStorageSpaceService space) =>
        {
            // Without the header the token store would read a form-typed body as a form to look for the token.
            if (!ctx.Request.Headers.ContainsKey(antiforgeryOptions.Value.HeaderName!) || !await antiforgery.IsRequestValidAsync(ctx))
                return Error(400, "The antiforgery token is missing or invalid. Reload the page.");

            if (ObjectKeyValidator.GetValidationError(key) is { } keyError)
                return Error(400, keyError);

            if (await metadata.GetBucketAsync(bucketName, IsPrivileged(ctx) ? null : GetCallerId(ctx), ctx.RequestAborted) is null)
                return Error(404, $"Bucket \"{bucketName}\" does not exist.");

            if (space.Get().IsBelowMinimum)
                return Error(507, "Not enough free disk space on the server.");

            // No early quota check on Content-Length: an answer before the body is read reaches a browser that is still
            // sending as a dropped connection, so the quota is checked once the staged file has its real size.
            try
            {
                await using var hashingStream = new HashingStream(ctx.Request.Body);
                await using var staged = await storage.StageAsync(bucketName, key, hashingStream, ctx.RequestAborted);
                var etag = hashingStream.GetETag();

                if (await quota.CheckWriteAsync(bucketName, key, staged.Size, ctx.RequestAborted) is { } exceeded)
                    return Error(507, $"Storage quota of the bucket owner exceeded ({FileHelper.FormatBytes(exceeded.RequestedBytes)} of {FileHelper.FormatBytes(exceeded.QuotaBytes)}).");

                var storagePath = await staged.CommitAsync(ctx.RequestAborted);

                // Not cancellable once the bytes are in place: the row must follow them even if the browser is gone.
                await metadata.SaveObjectAsync(new BlobObject
                {
                    BucketName = bucketName,
                    Key = key,
                    Size = staged.Size,
                    ContentType = string.IsNullOrEmpty(ctx.Request.ContentType) ? "application/octet-stream" : ctx.Request.ContentType,
                    ETag = etag,
                    StoragePath = storagePath
                }, GetCallerId(ctx));

                return Results.Ok(new { key, size = staged.Size, etag });
            }
            catch (BadHttpRequestException ex)
            {
                return Error(ex.StatusCode, ex.StatusCode == StatusCodes.Status413PayloadTooLarge
                    ? "The file is larger than the server accepts."
                    : ex.Message);
            }
            catch (Exception) when (ctx.RequestAborted.IsCancellationRequested)
            {
                // The browser cancelled or lost the connection; the staged file is already gone and nobody reads an answer.
                return Results.Empty;
            }
        }).RequireAuthorization();
    }
}
