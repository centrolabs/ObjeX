using System.Security.Cryptography;

namespace ObjeX.Api.S3;

/// <summary>The optional Content-MD5 request header: base64 of the 16-byte MD5 of the payload.</summary>
public static class ContentMd5
{
    /// <summary>False means the header is present but is not base64 of exactly 16 bytes, which is InvalidDigest.</summary>
    public static bool TryParse(string? header, out string? digestHex)
    {
        digestHex = null;
        if (string.IsNullOrEmpty(header)) return true;

        Span<byte> digest = stackalloc byte[16];
        if (!Convert.TryFromBase64String(header, digest, out var written) || written != 16)
            return false;

        digestHex = Convert.ToHexString(digest).ToLowerInvariant();
        return true;
    }

    /// <summary>True when no digest was sent or it equals the MD5 the server computed over the payload.</summary>
    public static bool Matches(string? digestHex, string etag) =>
        digestHex is null || string.Equals(digestHex, etag, StringComparison.OrdinalIgnoreCase);

    public static string ComputeHex(ReadOnlySpan<byte> payload) =>
        Convert.ToHexString(MD5.HashData(payload)).ToLowerInvariant();
}
