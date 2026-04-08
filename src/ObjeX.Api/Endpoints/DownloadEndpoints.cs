using System.IO.Compression;
using System.Security.Claims;

using ObjeX.Core.Interfaces;
using ObjeX.Core.Utilities;

namespace ObjeX.Api.Endpoints;

public static class DownloadEndpoints
{
    static string GetCallerId(HttpContext ctx) =>
        ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    static bool IsPrivileged(HttpContext ctx) =>
        ctx.User.IsInRole("Admin") || ctx.User.IsInRole("Manager");

    public static void MapDownloadEndpoints(this WebApplication app)
    {
        // Single-file download — used by the Blazor UI (cookie auth, port 9001)
        app.MapGet("/api/objects/{bucketName}/{*key}", async (
            string bucketName, string key, bool? download,
            HttpContext ctx, IMetadataService metadata, IObjectStorageService storage) =>
        {
            if (await metadata.GetBucketAsync(bucketName, IsPrivileged(ctx) ? null : GetCallerId(ctx)) is null)
                return Results.NotFound();

            var obj = await metadata.GetObjectAsync(bucketName, key);
            if (obj is null)
                return Results.NotFound();

            Stream stream = await storage.RetrieveAsync(bucketName, key);

            if (ctx.Request.Headers.ContainsKey("x-objex-verify-integrity"))
            {
                await using var hashingStream = new HashingStream(stream);
                var buffer = new MemoryStream();
                await hashingStream.CopyToAsync(buffer, ctx.RequestAborted);
                var computedETag = hashingStream.GetETag();

                if (computedETag != obj.ETag)
                    return Results.Problem($"Integrity check failed: stored ETag {obj.ETag} does not match computed {computedETag}.", statusCode: 500);

                buffer.Position = 0;
                stream = buffer;
            }

            var contentType = download == true ? "application/octet-stream" : obj.ContentType;
            var fileName = download == true ? Path.GetFileName(key) : null;
            var entityTag = new Microsoft.Net.Http.Headers.EntityTagHeaderValue($"\"{obj.ETag}\"");
            return Results.File(stream, contentType,
                fileDownloadName: fileName,
                lastModified: obj.UpdatedAt,
                entityTag: entityTag,
                enableRangeProcessing: true);
        }).RequireAuthorization();

        // ZIP download — folder/bucket via ?prefix=, or specific files via ?keys=a&keys=b
        app.MapGet("/api/objects/{bucketName}/download", async (
            string bucketName, string? prefix, string[]? keys,
            HttpContext ctx, IMetadataService metadata, IObjectStorageService storage,
            ILogger<IObjectStorageService> logger) =>
        {
            if (await metadata.GetBucketAsync(bucketName, IsPrivileged(ctx) ? null : GetCallerId(ctx)) is null)
            {
                ctx.Response.StatusCode = 404;
                return;
            }

            List<Core.Models.BlobObject> files;
            string zipName;

            if (keys is { Length: > 0 })
            {
                var objects = await Task.WhenAll(keys.Select(k => metadata.GetObjectAsync(bucketName, k)));
                files = objects.Where(o => o is not null && !o.Key.EndsWith("/")).Select(o => o!).ToList();
                zipName = "selection.zip";
            }
            else
            {
                var result = await metadata.ListObjectsAsync(bucketName, prefix, delimiter: null);
                files = result.Objects.Where(o => !o.Key.EndsWith("/")).ToList();
                zipName = string.IsNullOrEmpty(prefix) ? $"{bucketName}.zip" : $"{prefix.TrimEnd('/')}.zip";
            }

            // ZipArchive.Dispose() writes data descriptors and central directory synchronously —
            // Kestrel blocks sync IO by default, so we must opt in for this endpoint.
            var syncIoFeature = ctx.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpBodyControlFeature>();
            if (syncIoFeature is not null) syncIoFeature.AllowSynchronousIO = true;

            ctx.Response.ContentType = "application/zip";
            ctx.Response.Headers["Content-Disposition"] = $"attachment; filename=\"{zipName}\"";

            using (var zip = new ZipArchive(ctx.Response.Body, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var obj in files)
                {
                    try
                    {
                        await using var fileStream = await storage.RetrieveAsync(bucketName, obj.Key);
                        var entry = zip.CreateEntry(obj.Key, CompressionLevel.Fastest);
                        using var entryStream = entry.Open();
                        await fileStream.CopyToAsync(entryStream, ctx.RequestAborted);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Skipping {Key} in ZIP download — blob missing or unreadable", obj.Key);
                    }
                }
            }

            await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
        }).RequireAuthorization();
    }
}
