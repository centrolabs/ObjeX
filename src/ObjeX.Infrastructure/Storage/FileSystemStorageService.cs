using ObjeX.Core.Interfaces;

namespace ObjeX.Infrastructure.Storage;

public class FileSystemStorageService : IObjectStorageService
{
    public FileSystemStorageService(string basePath)
    {
        Directory.CreateDirectory(basePath);
    }

    public Task<string> StoreAsync(string bucketName, string key, Stream data, CancellationToken ctk = default)
    {
        throw new NotImplementedException();
    }

    public Task<Stream> RetrieveAsync(string bucketName, string key, CancellationToken ctk = default)
    {
        throw new NotImplementedException();
    }

    public Task DeleteAsync(string bucketName, string key, CancellationToken ctk = default)
    {
        throw new NotImplementedException();
    }

    public Task<bool> ExistsAsync(string bucketName, string key, CancellationToken ctk = default)
    {
        throw new NotImplementedException();
    }

    public Task<long> GetSizeAsync(string bucketName, string key, CancellationToken ctk = default)
    {
        throw new NotImplementedException();
    }
}