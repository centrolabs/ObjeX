using System.Security.Cryptography;

using ObjeX.Core.Utilities;

namespace ObjeX.Tests.Unit;

public class HashingStreamTests
{
    [Fact]
    public async Task Known_Content_Produces_Correct_ETag()
    {
        var content = "Hello, ObjeX!"u8.ToArray();
        var expected = Convert.ToHexString(MD5.HashData(content)).ToLowerInvariant();

        using var source = new MemoryStream(content);
        await using var hashingStream = new HashingStream(source);

        var buffer = new byte[4096];
        int totalRead = 0, bytesRead;
        while ((bytesRead = await hashingStream.ReadAsync(buffer.AsMemory(totalRead))) > 0)
            totalRead += bytesRead;

        Assert.Equal(content.Length, totalRead);
        Assert.Equal(expected, hashingStream.GetETag());
    }

    [Fact]
    public async Task Empty_Stream_Produces_Valid_ETag()
    {
        var expected = Convert.ToHexString(MD5.HashData([])).ToLowerInvariant();

        using var source = new MemoryStream([]);
        await using var hashingStream = new HashingStream(source);

        var buffer = new byte[16];
        var read = await hashingStream.ReadAsync(buffer);

        Assert.Equal(0, read);
        Assert.Equal(expected, hashingStream.GetETag());
    }

    [Fact]
    public async Task Large_Content_Produces_Correct_ETag()
    {
        var content = new byte[1024 * 1024]; // 1MB
        Random.Shared.NextBytes(content);
        var expected = Convert.ToHexString(MD5.HashData(content)).ToLowerInvariant();

        using var source = new MemoryStream(content);
        await using var hashingStream = new HashingStream(source);

        using var dest = new MemoryStream();
        await hashingStream.CopyToAsync(dest);

        Assert.Equal(expected, hashingStream.GetETag());
        Assert.Equal(content, dest.ToArray());
    }
}
