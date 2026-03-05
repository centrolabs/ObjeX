using System.Security.Cryptography;
using ObjeX.Core.Interfaces;
using ObjeX.Core.Models;

namespace ObjeX.Api.Endpoints;

public static class ObjectEndpoints
{
    public static void MapObjectEndpoints(this WebApplication app)
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

            var stream = request.Body;
            var contentType = request.ContentType ?? "application/octet-stream";

            var storagePath = await storage.StoreAsync(bucketName, key, stream);
            var size = await storage.GetSizeAsync(bucketName, key);
            var etag = await ComputeETag(stream); 

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

        objects.MapGet("/", async (string bucketName, IMetadataService metadata) =>
        {
            if (!await metadata.ExistsBucketAsync(bucketName))
            {
                return Results.NotFound(new { error = "Bucket not found" });
            }

            var objectList = await metadata.ListObjectsAsync(bucketName);
            return Results.Ok(objectList);
        });
    }

    private static async Task<string> ComputeETag(Stream stream)
    {
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        ms.Position = 0;
    
        using var md5 = MD5.Create();
        var hash = await md5.ComputeHashAsync(ms);
    
        return Convert.ToHexString(hash).ToLower();
    }
}