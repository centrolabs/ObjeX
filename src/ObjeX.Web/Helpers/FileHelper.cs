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

    public static string FolderZipUrl(string bucket, string prefix) =>
        $"/api/objects/{Uri.EscapeDataString(bucket)}/download?prefix={Uri.EscapeDataString(prefix)}";
}
