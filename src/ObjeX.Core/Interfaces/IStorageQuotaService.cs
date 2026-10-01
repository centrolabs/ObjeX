namespace ObjeX.Core.Interfaces;

public record StorageQuotaStatus(long UsedBytes, long? QuotaBytes)
{
    public bool HasQuota => QuotaBytes is > 0;
    public double UsedPercent => HasQuota ? (double)UsedBytes / QuotaBytes!.Value * 100 : 0;
}

/// <summary>A write the bucket owner's quota refuses: the owner's usage including the write, and the quota.</summary>
public record QuotaExceeded(long RequestedBytes, long QuotaBytes);

public interface IStorageQuotaService
{
    /// <summary>Used = size of the user's buckets. Quota = the per-user value, else the global default for the User role, else unlimited.</summary>
    Task<StorageQuotaStatus> GetAsync(string userId, CancellationToken ctk = default);

    /// <summary>
    /// Null when writing newSize bytes under the key fits the bucket owner's quota. The owner pays whoever uploads,
    /// and an overwrite is charged only its growth over the stored size. An unknown bucket is never refused here.
    /// </summary>
    Task<QuotaExceeded?> CheckWriteAsync(string bucketName, string key, long newSize, CancellationToken ctk = default);

    /// <summary>
    /// Runs write, the commit and the row, unless <see cref="CheckWriteAsync"/> refuses it. For an owner with a quota the check and
    /// the write run one at a time, so parallel uploads cannot pass the check together; an owner without one writes in parallel.
    /// </summary>
    Task<QuotaExceeded?> WriteWithinQuotaAsync(string bucketName, string key, long newSize, Func<Task> write, CancellationToken ctk = default);
}
