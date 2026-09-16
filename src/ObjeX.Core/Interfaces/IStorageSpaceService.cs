namespace ObjeX.Core.Interfaces;

public record StorageSpaceStatus(long FreeBytes, long TotalBytes, long MinimumFreeBytes)
{
    public bool IsBelowMinimum => FreeBytes <= MinimumFreeBytes;
    public bool IsNearMinimum => !IsBelowMinimum && FreeBytes <= MinimumFreeBytes * 2;
    public long UsedBytes => TotalBytes - FreeBytes;
    public double UsedPercent => TotalBytes > 0 ? (double)UsedBytes / TotalBytes * 100 : 0;
}

public interface IStorageSpaceService
{
    /// <summary>Free and total space of the volume holding the blob root, plus the threshold below which uploads are rejected.</summary>
    StorageSpaceStatus Get();
}
