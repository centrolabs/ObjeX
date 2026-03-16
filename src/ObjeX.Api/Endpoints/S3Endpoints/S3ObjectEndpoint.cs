using ObjeX.Api.S3;
using ObjeX.Core.Interfaces;
using ObjeX.Core.Models;
using ObjeX.Core.Utilities;
using ObjeX.Core.Validation;
using ObjeX.Infrastructure.Storage;

namespace ObjeX.Api.Endpoints.S3Endpoints;

public static class S3ObjectEndpoint
{
    public static void MapS3ObjectEndpoints(this WebApplication app, RouteGroupBuilder s3)
    {
        s3.MapPut("/{bucket}/{*key}", async (
            string bucket,
            string key,
            HttpRequest request,
            IConfiguration config,
            IMetadataService metadata,
            IObjectStorageService storage,
            FileSystemStorageService fs) =>
        {
            if (ObjectKeyValidator.GetValidationError(key) is { } keyError)
                return S3Xml.Error(S3Errors.InvalidArgument, keyError);

            if (!await metadata.ExistsBucketAsync(bucket))
                return S3Xml.Error(S3Errors.NoSuchBucket, "The specified bucket does not exist.", 404);

            var minFreeBytes = config.GetValue<long>("Storage:MinimumFreeDiskBytes", 500 * 1024 * 1024);
            if (fs.GetAvailableFreeSpace() < minFreeBytes)
                return S3Xml.Error(S3Errors.EntityTooLarge, "Insufficient disk space.", 507);

            var contentType = request.ContentType ?? "application/octet-stream";

            await using var hashingStream = new HashingStream(request.Body);
            var storagePath = await storage.StoreAsync(bucket, key, hashingStream);
            var size = await storage.GetSizeAsync(bucket, key);
            var etag = hashingStream.GetETag();

            await metadata.SaveObjectAsync(new BlobObject
            {
                BucketName = bucket,
                Key = key,
                Size = size,
                ContentType = contentType,
                ETag = etag,
                StoragePath = storagePath
            });

            return Results.Created($"/{bucket}/{key}", null);
        });

        s3.MapGet("/{bucket}/{*key}", async (
            string bucket,
            string key,
            bool? download,
            HttpContext ctx,
            IMetadataService metadata,
            IObjectStorageService storage) =>
        {
            if (ObjectKeyValidator.GetValidationError(key) is { } keyError)
                return S3Xml.Error(S3Errors.InvalidArgument, keyError);

            var obj = await metadata.GetObjectAsync(bucket, key);
            if (obj is null)
                return S3Xml.Error(S3Errors.NoSuchKey, "The specified key does not exist.", 404);

            ctx.Response.Headers.ETag = $"\"{obj.ETag}\"";
            ctx.Response.Headers.ContentLength = obj.Size;
            ctx.Response.Headers.LastModified = obj.UpdatedAt.ToString("R");
            var stream = await storage.RetrieveAsync(bucket, key);
            var fileName = Path.GetFileName(obj.Key);

            // ?download=true forces browser download regardless of content type
            var contentType = download == true ? "application/octet-stream" : obj.ContentType;
            var downloadName = download == true ? fileName : null;
            return Results.File(stream, contentType, fileDownloadName: downloadName);
        });

        s3.MapMethods("/{bucket}/{*key}", ["HEAD"], async (
            string bucket,
            string key,
            HttpContext ctx,
            IMetadataService metadata) =>
        {
            var obj = await metadata.GetObjectAsync(bucket, key);
            if (obj is null)
                return S3Xml.Error(S3Errors.NoSuchKey, "The specified key does not exist.", 404);

            ctx.Response.Headers.ETag = $"\"{obj.ETag}\"";
            ctx.Response.Headers.ContentLength = obj.Size;
            ctx.Response.Headers.ContentType = obj.ContentType;
            ctx.Response.Headers.LastModified = obj.UpdatedAt.ToString("R");
            return Results.Ok();
        });

        s3.MapDelete("/{bucket}/{*key}", async (
            string bucket,
            string key,
            IMetadataService metadata,
            IObjectStorageService storage) =>
        {
            // S3 spec: DELETE returns 204 even if object does not exist
            if (await metadata.ExistsObjectAsync(bucket, key))
            {
                await storage.DeleteAsync(bucket, key);
                await metadata.DeleteObjectAsync(bucket, key);
            }
            return Results.StatusCode(204);
        });
    }
}
