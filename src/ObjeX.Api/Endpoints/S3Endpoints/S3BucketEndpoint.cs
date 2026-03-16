using ObjeX.Api.S3;
using ObjeX.Core.Interfaces;

namespace ObjeX.Api.Endpoints.S3Endpoints;

public static class S3BucketEndpoint
{
    public static void MapS3BucketEndpoints(this WebApplication app, RouteGroupBuilder s3)
    {
        s3.MapGet("/", async (IMetadataService metadata) =>
        {
            var buckets = await metadata.ListBucketsAsync();
            return S3Xml.ListBuckets(buckets);
        });

        s3.MapMethods("/{bucket}", ["HEAD"], async (string bucket, IMetadataService metadata) =>
        {
            var exists = await metadata.ExistsBucketAsync(bucket);
            return exists ? Results.Ok() : Results.NotFound();
        });

        s3.MapPut("/{bucket}", async (string bucket, IMetadataService metadata) =>
        {
            try
            {
                await metadata.CreateBucketAsync(new Core.Models.Bucket { Name = bucket });
                return Results.Ok();
            }
            catch (ArgumentException ex)
            {
                return S3Xml.Error(S3Errors.InvalidBucketName, ex.Message);
            }
        });

        s3.MapDelete("/{bucket}", async (string bucket, IMetadataService metadata) =>
        {
            var exists = await metadata.ExistsBucketAsync(bucket);
            if (!exists)
                return S3Xml.Error(S3Errors.NoSuchBucket, "The specified bucket does not exist.", 404);

            var objects = await metadata.ListObjectsAsync(bucket);
            if (objects.Objects.Any())
                return S3Xml.Error(S3Errors.BucketNotEmpty, "The bucket you tried to delete is not empty.", 409);

            await metadata.DeleteBucketAsync(bucket);
            return Results.StatusCode(204);
        });

        s3.MapGet("/{bucket}", async (string bucket, string? prefix, string? delimiter, IMetadataService metadata) =>
        {
            var exists = await metadata.ExistsBucketAsync(bucket);
            if (!exists)
                return S3Xml.Error(S3Errors.NoSuchBucket, "The specified bucket does not exist.", 404);

            var result = await metadata.ListObjectsAsync(bucket, prefix, delimiter);
            return S3Xml.ListObjects(bucket, result.Objects, result.CommonPrefixes, prefix, delimiter);
        });
    }
}
