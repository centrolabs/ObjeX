namespace ObjeX.Web.Helpers;

public static class FileHelper
{
    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }

    public static string ObjectUrl(string bucket, string key, bool download = false)
    {
        var url = $"/api/objects/{Uri.EscapeDataString(bucket)}/{KeyPath(key)}";
        return download ? url + "?download=true" : url;
    }

    public static string UploadUrl(string bucket, string key) =>
        $"/api/upload/{Uri.EscapeDataString(bucket)}/{KeyPath(key)}";

    /// <summary>Keys may contain URL syntax (#, ?, %, &amp;); each segment is encoded, slashes stay so the URL reads like the key.</summary>
    static string KeyPath(string key) => string.Join("/", key.Split('/').Select(Uri.EscapeDataString));

    /// <summary>The folder that holds a key, as a page link. Keys may contain URL syntax, so each segment is encoded while the slashes stay readable.</summary>
    public static string FolderUrl(string bucket, string key)
    {
        var url = $"/buckets/{Uri.EscapeDataString(bucket)}";
        var cut = key.LastIndexOf('/');
        if (cut < 0) return url;

        var folder = key[..(cut + 1)];
        return $"{url}?prefix={string.Join("/", folder.Split('/').Select(Uri.EscapeDataString))}";
    }

    public static string FolderZipUrl(string bucket, string prefix) =>
        $"/api/objects/{Uri.EscapeDataString(bucket)}/download?prefix={Uri.EscapeDataString(prefix)}";
}
