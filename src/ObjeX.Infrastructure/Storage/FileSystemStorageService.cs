using ObjeX.Core.Interfaces;

namespace ObjeX.Infrastructure.Storage;

public class FileSystemStorageService : IObjectStorageService
{
    private readonly string _basePath;
    
    public FileSystemStorageService(string basePath)
    {
        _basePath = basePath;
        Directory.CreateDirectory(_basePath);
    }

    public async Task<string> StoreAsync(string bucketName, string key, Stream data, CancellationToken ctk = default)
    {
        var bucketPath = Path.Combine(_basePath, bucketName);
        Directory.CreateDirectory(bucketPath);

        var filePath = Path.Combine(bucketPath, SanitizeKey(key));
        var directory = Path.GetDirectoryName(filePath);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        await using var fileStream = File.Create(filePath);
        await data.CopyToAsync(fileStream, ctk);

        return filePath;    }

    public async Task<Stream> RetrieveAsync(string bucketName, string key, CancellationToken ctk = default)
    {
        var filePath = GetFilePath(bucketName, key);
        
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Object not found: {bucketName}/{key}");
        }

        return await Task.FromResult<Stream>(File.OpenRead(filePath));
    }

    public Task DeleteAsync(string bucketName, string key, CancellationToken ctk = default)
    {
        var filePath = GetFilePath(bucketName, key);
        
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string bucketName, string key, CancellationToken ctk = default)
    {
        var filePath = GetFilePath(bucketName, key);
        return Task.FromResult(File.Exists(filePath));
    }

    public Task<long> GetSizeAsync(string bucketName, string key, CancellationToken ctk = default)
    {
        var filePath = GetFilePath(bucketName, key);
        
        if (!File.Exists(filePath))
        {
            return Task.FromResult(0L);
        }

        var fileInfo = new FileInfo(filePath);
        return Task.FromResult(fileInfo.Length);
    }
    
    private string GetFilePath(string bucketName, string key)
    {
        return Path.Combine(_basePath, bucketName, SanitizeKey(key));
    }

    private static string SanitizeKey(string key)
    {
        return key.Replace("..", "").Replace("\\", "/");
    }
}