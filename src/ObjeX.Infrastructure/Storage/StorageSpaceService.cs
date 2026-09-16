using ObjeX.Core.Interfaces;

namespace ObjeX.Infrastructure.Storage;

public class StorageSpaceService : IStorageSpaceService
{
    private readonly string _basePath;
    private readonly long _minimumFreeBytes;

    public StorageSpaceService(string basePath, long minimumFreeBytes)
    {
        _basePath = basePath;
        _minimumFreeBytes = minimumFreeBytes;
        // DriveInfo stats the path itself, so the blob root must exist before the first read.
        Directory.CreateDirectory(_basePath);
    }

    public StorageSpaceStatus Get()
    {
        var drive = new DriveInfo(_basePath);
        return new StorageSpaceStatus(drive.AvailableFreeSpace, drive.TotalSize, _minimumFreeBytes);
    }

    internal static long AvailableFreeSpace(string path) => new DriveInfo(path).AvailableFreeSpace;
}
