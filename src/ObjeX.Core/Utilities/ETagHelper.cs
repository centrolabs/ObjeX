using System.Security.Cryptography;

namespace ObjeX.Core.Utilities;

public static class ETagHelper
{
    public static async Task<string> ComputeAsync(Stream stream)
    {
        using var md5 = MD5.Create();
        var hash = await md5.ComputeHashAsync(stream);
        return Convert.ToHexString(hash).ToLower();
    }
}
