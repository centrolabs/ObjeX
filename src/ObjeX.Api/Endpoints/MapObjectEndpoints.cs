using System.IO.Compression;

using ObjeX.Core.Interfaces;
using ObjeX.Core.Models;
using ObjeX.Core.Utilities;

namespace ObjeX.Api.Endpoints;

public static class ObjectEndpoints
{
    public static RouteGroupBuilder MapObjectEndpoints(this WebApplication app)
    {
        var objects = app.MapGroup("api/objects/{bucketName}").WithTags("Objects");

        objects.MapPut("/{*key}", async (
            string bucketName,
            string key,
            HttpRequest request,
            IMetadataService metadata,
            IObjectStorageService storage) =>
        {
            if (!await metadata.ExistsBucketAsync(bucketName))
            {
                return Results.NotFound(new { error = "Bucket not found" });
            }

            var contentType = request.ContentType ?? "application/octet-stream";

            await using var hashingStream = new HashingStream(request.Body);
            var storagePath = await storage.StoreAsync(bucketName, key, hashingStream);
            var size = await storage.GetSizeAsync(bucketName, key);
            var etag = hashingStream.GetETag();

            var blobObject = new BlobObject
            {
                BucketName = bucketName,
                Key = key,
                Size = size,
                ContentType = contentType,
                ETag = etag,
                StoragePath = storagePath
            };

            await metadata.SaveObjectAsync(blobObject);

            return Results.Ok(new { key, etag, size });
        });

        objects.MapGet("/{*key}", async (
            string bucketName,
            string key,
            IMetadataService metadata,
            IObjectStorageService storage) =>
        {
            var obj = await metadata.GetObjectAsync(bucketName, key);
            if (obj is null)
            {
                return Results.NotFound(new { error = "Object not found" });
            }

            var stream = await storage.RetrieveAsync(bucketName, key);
            return Results.File(stream, obj.ContentType, obj.Key);
        });

        objects.MapDelete("/{*key}", async (
            string bucketName,
            string key,
            IMetadataService metadata,
            IObjectStorageService storage) =>
        {
            if (!await metadata.ExistsObjectAsync(bucketName, key))
            {
                return Results.NotFound(new { error = "Object not found" });
            }

            await storage.DeleteAsync(bucketName, key);
            await metadata.DeleteObjectAsync(bucketName, key);

            return Results.NoContent();
        });

        objects.MapGet("/download", async (string bucketName, string? prefix, IMetadataService metadata, IObjectStorageService storage) =>
        {
            if (!await metadata.ExistsBucketAsync(bucketName))
                return Results.NotFound(new { error = "Bucket not found" });

            var result = await metadata.ListObjectsAsync(bucketName, prefix, delimiter: null);
            var files = result.Objects.Where(o => !o.Key.EndsWith("/")).ToList();

            var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var obj in files)
                {
                    var entry = zip.CreateEntry(obj.Key, CompressionLevel.Fastest);
                    await using var fileStream = await storage.RetrieveAsync(bucketName, obj.Key);
                    await using var entryStream = entry.Open();
                    await fileStream.CopyToAsync(entryStream);
                }
            }
            ms.Position = 0;

            var zipName = string.IsNullOrEmpty(prefix)
                ? $"{bucketName}.zip"
                : $"{prefix.TrimEnd('/')}.zip";

            return Results.File(ms, "application/zip", zipName);
        });

        objects.MapGet("/", async (string bucketName, string? prefix, string? delimiter, IMetadataService metadata) =>
        {
            if (!await metadata.ExistsBucketAsync(bucketName))
            {
                return Results.NotFound(new { error = "Bucket not found" });
            }

            var result = await metadata.ListObjectsAsync(bucketName, prefix, delimiter);
            return Results.Ok(result);
        });
        
        return objects;
    }
}