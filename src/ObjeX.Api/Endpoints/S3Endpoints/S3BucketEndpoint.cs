using System.Security.Claims;

using Microsoft.EntityFrameworkCore;

using ObjeX.Api.S3;
using ObjeX.Core.Interfaces;
using ObjeX.Infrastructure.Data;

namespace ObjeX.Api.Endpoints.S3Endpoints;

public static class S3BucketEndpoint
{
    static string GetCallerId(HttpContext ctx) =>
        ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    static bool IsPrivileged(HttpContext ctx) =>
        ctx.User.IsInRole("Admin") || ctx.User.IsInRole("Manager");

    public static void MapS3BucketEndpoints(this RouteGroupBuilder s3)
    {
        s3.MapGet("/", async (HttpContext ctx, IMetadataService metadata) =>
        {
            var buckets = await metadata.ListBucketsAsync(IsPrivileged(ctx) ? null : GetCallerId(ctx));
            return S3Xml.ListBuckets(buckets);
        });

        s3.MapMethods("/{bucket}", ["HEAD"], async (string bucket, HttpContext ctx, IMetadataService metadata) =>
        {
            var b = await metadata.GetBucketAsync(bucket, IsPrivileged(ctx) ? null : GetCallerId(ctx));
            return b is not null ? Results.Ok() : Results.NotFound();
        });

        s3.MapPut("/{bucket}", async (string bucket, HttpRequest request, HttpContext ctx, IMetadataService metadata) =>
        {
            if (S3Subresources.IsUnsupportedOnBucket(request))
                return S3Subresources.NotImplemented();

            try
            {
                await metadata.CreateBucketAsync(new Core.Models.Bucket { Name = bucket, OwnerId = GetCallerId(ctx) }, GetCallerId(ctx));
                return Results.Ok();
            }
            catch (ArgumentException ex)
            {
                return S3Xml.Error(S3Errors.InvalidBucketName, ex.Message);
            }
            catch (InvalidOperationException)
            {
                return S3Xml.Error(S3Errors.BucketAlreadyExists, $"The bucket '{bucket}' already exists.", 409);
            }
        });

        s3.MapDelete("/{bucket}", async (string bucket, HttpRequest request, HttpContext ctx, IMetadataService metadata,
            IObjectStorageService storage, ILogger<IObjectStorageService> logger) =>
        {
            if (S3Subresources.IsUnsupportedOnBucket(request))
                return S3Subresources.NotImplemented();

            var callerId = GetCallerId(ctx);
            var privileged = IsPrivileged(ctx);

            if (await metadata.GetBucketAsync(bucket, privileged ? null : callerId) is null)
                return S3Xml.Error(S3Errors.NoSuchBucket, "The specified bucket does not exist.", 404);

            var objects = await metadata.ListObjectsAsync(bucket, maxKeys: 1);
            if (objects.Objects.Any())
                return S3Xml.Error(S3Errors.BucketNotEmpty, "The bucket you tried to delete is not empty.", 409);

            await metadata.DeleteBucketAsync(bucket, callerId, privileged, callerId);
            try
            {
                await storage.DeleteBucketAsync(bucket, ctx.RequestAborted);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Blob folder of bucket {Bucket} could not be deleted and is left for the orphan cleanup job", bucket);
            }
            return Results.StatusCode(204);
        });

        s3.MapGet("/{bucket}", async (string bucket, string? prefix, string? delimiter, HttpRequest request, HttpContext ctx, IMetadataService metadata, ObjeXDbContext db) =>
        {
            var b = await metadata.GetBucketAsync(bucket, IsPrivileged(ctx) ? null : GetCallerId(ctx));
            if (b is null)
                return S3Xml.Error(S3Errors.NoSuchBucket, "The specified bucket does not exist.", 404);

            if (request.Query.ContainsKey("location"))
                return S3Xml.BucketLocation();

            if (request.Query.ContainsKey("uploads"))
            {
                var uploads = await db.MultipartUploads
                    .Where(u => u.BucketName == bucket)
                    .OrderByDescending(u => u.CreatedAt)
                    .ToListAsync();
                return S3Xml.ListMultipartUploads(bucket, uploads);
            }

            // ObjeX buckets are never versioned: each object is its own "null" version, as on AWS.
            if (request.Query.ContainsKey("versioning"))
                return S3Xml.VersioningConfiguration();

            if (S3Subresources.IsUnsupportedOnBucket(request))
                return S3Subresources.NotImplemented();

            var query = request.Query;
            var maxKeysRaw = query["max-keys"].FirstOrDefault();
            var maxKeys = MaxKeys;
            if (maxKeysRaw is not null && (!int.TryParse(maxKeysRaw, out maxKeys) || maxKeys < 0))
                return S3Xml.Error(S3Errors.InvalidArgument, "Provided max-keys not an integer or within integer range.");
            maxKeys = Math.Min(maxKeys, MaxKeys);

            var encodingType = query["encoding-type"].FirstOrDefault();
            if (encodingType is not null && encodingType != "url")
                return S3Xml.Error(S3Errors.InvalidArgument, "Invalid Encoding Method specified in Request.");

            var listing = new S3Xml.Listing(bucket, prefix, delimiter, maxKeys, encodingType == "url", b.OwnerId, b.Owner?.UserName ?? b.OwnerId);

            if (query.ContainsKey("versions"))
            {
                var keyMarker = query["key-marker"].FirstOrDefault();
                var versions = await metadata.ListObjectsAsync(bucket, prefix, delimiter, keyMarker, maxKeys);
                return S3Xml.ListObjectVersions(versions, listing, keyMarker);
            }

            if (query["list-type"] == "2")
            {
                var continuationToken = query["continuation-token"].FirstOrDefault();
                var startAfter = query["start-after"].FirstOrDefault();
                var after = startAfter;
                if (continuationToken is not null && !ContinuationToken.TryDecode(continuationToken, out after))
                    return S3Xml.Error(S3Errors.InvalidArgument, "The continuation token provided is incorrect.");

                var pageV2 = await metadata.ListObjectsAsync(bucket, prefix, delimiter, after, maxKeys);
                return S3Xml.ListObjectsV2(pageV2, listing, continuationToken, startAfter, query["fetch-owner"] == "true");
            }

            var marker = query["marker"].FirstOrDefault();
            var page = await metadata.ListObjectsAsync(bucket, prefix, delimiter, marker, maxKeys);
            return S3Xml.ListObjects(page, listing, marker);
        });
    }

    /// <summary>The S3 page size cap; larger max-keys values are lowered to it, as on AWS.</summary>
    private const int MaxKeys = 1000;
}
