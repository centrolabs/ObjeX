namespace ObjeX.Core.Interfaces;

public record StorageQuotaStatus(long UsedBytes, long? QuotaBytes)
{
    public bool HasQuota => QuotaBytes is > 0;
    public double UsedPercent => HasQuota ? (double)UsedBytes / QuotaBytes!.Value * 100 : 0;
}

public interface IStorageQuotaService
{
    /// <summary>Used = size of the user's buckets. Quota = the per-user value, else the global default for the User role, else unlimited.</summary>
    Task<StorageQuotaStatus> GetAsync(string userId, CancellationToken ctk = default);
}
