namespace ObjeX.Core.Utilities;

/// <summary>Tells a multipart ETag apart from a plain one: multipart is MD5 of the concatenated part MD5s plus "-N", so re-hashing the assembled blob never reproduces it.</summary>
public static class ETags
{
    public static bool IsMultipart(string? etag) =>
        etag is not null && etag.Contains('-');
}
