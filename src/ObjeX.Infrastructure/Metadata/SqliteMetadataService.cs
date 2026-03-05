using ObjeX.Core.Interfaces;
using ObjeX.Core.Models;

namespace ObjeX.Infrastructure.Metadata;

public class SqliteMetadataService : IMetadataService
{
    public Task<List<Bucket>> CreateBucketAsync(Bucket bucket, CancellationToken ctk = default)
    {
        throw new NotImplementedException();
    }

    public Task<Bucket> GetBucketAsync(string bucketName, CancellationToken ctk = default)
    {
        throw new NotImplementedException();
    }

    public Task<IEnumerable<Bucket>> ListBucketsAsync(CancellationToken ctk = default)
    {
        throw new NotImplementedException();
    }

    public Task DeleteBucketAsync(string bucketName, CancellationToken ctk = default)
    {
        throw new NotImplementedException();
    }

    public Task<bool> ExistsBucketAsync(string bucketName, CancellationToken ctk = default)
    {
        throw new NotImplementedException();
    }

    public Task<BlobObject> SaveObjectAsync(BlobObject blobObject, CancellationToken ctk = default)
    {
        throw new NotImplementedException();
    }

    public Task<BlobObject?> GetObjectAsync(string bucketName, string key, CancellationToken ctk = default)
    {
        throw new NotImplementedException();
    }

    public Task<IEnumerable<BlobObject>> ListObjectsAsync(string bucketName, CancellationToken ctk = default)
    {
        throw new NotImplementedException();
    }

    public Task DeleteObjectAsync(string bucketName, string key, CancellationToken ctk = default)
    {
        throw new NotImplementedException();
    }

    public Task<bool> ExistsObjectAsync(string bucketName, string key, CancellationToken ctk = default)
    {
        throw new NotImplementedException();
    }

    public Task UpdateBucketStatsAsync(string bucketName, CancellationToken ctk = default)
    {
        throw new NotImplementedException();
    }
}