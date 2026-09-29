using System.Buffers.Text;
using System.Text;

namespace ObjeX.Api.S3;

/// <summary>The opaque ListObjectsV2 continuation token: the last key or common prefix of the page, base64url-encoded.</summary>
public static class ContinuationToken
{
    public static string Encode(string marker) => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(marker));

    public static bool TryDecode(string token, out string marker)
    {
        marker = string.Empty;
        try
        {
            marker = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(token));
            return marker.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
