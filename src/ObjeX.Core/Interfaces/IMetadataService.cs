using ObjeX.Core.Models;

namespace ObjeX.Core.Interfaces;

/// <summary>
/// Database Operations
/// Responsibility: Track object information in a database 
/// </summary>
public interface IMetadataService
{
    Task<Bucket> CreateBucketAsync(Bucket bucket, string? auditUserId = null, CancellationToken ctk = default);
    Task<Bucket?> GetBucketAsync(string bucketName, string? ownerFilter = null, CancellationToken ctk = default);
    Task<IEnumerable<Bucket>> ListBucketsAsync(string? ownerFilter = null, CancellationToken ctk = default);
    Task DeleteBucketAsync(string bucketName, string userId, bool isPrivileged, string? auditUserId = null, CancellationToken ctk = default);
    Task<bool> ExistsBucketAsync(string bucketName, CancellationToken ctk = default);

    Task<BlobObject> SaveObjectAsync(BlobObject blobObject, string? auditUserId = null, CancellationToken ctk = default);
    Task<BlobObject?> GetObjectAsync(string bucketName, string key, CancellationToken ctk = default);
    Task<ListObjectsResult> ListObjectsAsync(string bucketName, string? prefix = null, string? delimiter = null, CancellationToken ctk = default);
    /// <summary>
    /// Keys under <paramref name="prefix"/> matching <paramref name="term"/> case-insensitively; placeholders excluded.
    /// <c>*</c> matches any run of characters, <c>?</c> exactly one, <c>%</c>, <c>_</c> and <c>\</c> are literal.
    /// A term without a wildcard matches anywhere, a term with one is anchored at the end.
    /// </summary>
    Task<IReadOnlyList<BlobObject>> SearchObjectsAsync(string bucketName, string? prefix, string term, int limit, CancellationToken ctk = default);
    Task<IEnumerable<BlobObject>> ListAllObjectsAsync(CancellationToken ctk = default);
    Task DeleteObjectAsync(string bucketName, string key, string? auditUserId = null, CancellationToken ctk = default);
    /// <summary>Deletes the given keys in one transaction and returns how many rows existed; unknown keys are ignored, as in S3.</summary>
    Task<int> DeleteObjectsAsync(string bucketName, IEnumerable<string> keys, string? auditUserId = null, CancellationToken ctk = default);
    Task<bool> ExistsObjectAsync(string bucketName, string key, CancellationToken ctk = default);

    /// <summary>Full recount for repair; writes adjust ObjectCount and TotalSize by the delta of the changed object.</summary>
    Task UpdateBucketStatsAsync(string bucketName, CancellationToken ctk = default);

    Task<IEnumerable<ContentTypeStats>> GetContentTypeStatsAsync(IEnumerable<string>? bucketNames = null, CancellationToken ctk = default);
}

public record ContentTypeStats(string ContentType, int Count, long TotalSize);