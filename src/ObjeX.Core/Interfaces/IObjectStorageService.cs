namespace ObjeX.Core.Interfaces;

/// <summary>
/// Physical Blob Storage
/// Responsibility: Read/write actual file bytes to disk or cloud
/// </summary>
public interface IObjectStorageService
{
    Task<string> StoreAsync(string bucketName, string key, Stream data, CancellationToken ctk = default);
    Task<Stream> RetrieveAsync(string bucketName, string key, CancellationToken ctk = default);
    Task DeleteAsync(string bucketName, string key, CancellationToken ctk = default);
    Task<bool> ExistsAsync(string bucketName, string key, CancellationToken ctk = default);
    Task<long> GetSizeAsync(string bucketName, string key, CancellationToken ctk = default);
}