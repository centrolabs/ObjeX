namespace ObjeX.Core.Utilities;

/// <summary>Content-Types the download endpoint serves inline and the UI offers a preview for; the stored type is chosen by the uploader, everything else downloads.</summary>
public static class InlineMediaTypes
{
    public static string MediaType(string contentType) => contentType.Split(';')[0].Trim().ToLowerInvariant();

    public static bool IsInlineSafe(string contentType)
    {
        var mediaType = MediaType(contentType);
        return mediaType is "image/png" or "image/jpeg" or "image/gif" or "image/webp" or "image/avif"
                or "image/bmp" or "image/x-icon" or "image/vnd.microsoft.icon"
                or "application/pdf" or "text/plain"
            || mediaType.StartsWith("video/", StringComparison.Ordinal)
            || mediaType.StartsWith("audio/", StringComparison.Ordinal);
    }
}
