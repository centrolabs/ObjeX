using System.Text;

using ObjeX.Api.S3;

namespace ObjeX.Tests.Unit;

/// <summary>
/// The decoder must never size a buffer after the chunk header a client sends — the header is
/// attacker-controlled and says nothing about how much data actually follows.
/// </summary>
public class AwsChunkedStreamTests
{
    /// <summary>Frames <paramref name="data"/> the way an SDK does: signed chunks or unsigned chunks with a checksum trailer.</summary>
    private static byte[] Frame(byte[] data, int chunkSize, bool signedChunks = true)
    {
        var sig = signedChunks ? ";chunk-signature=" + new string('0', 64) : "";
        using var ms = new MemoryStream();
        for (var offset = 0; offset < data.Length; offset += chunkSize)
        {
            var len = Math.Min(chunkSize, data.Length - offset);
            ms.Write(Encoding.ASCII.GetBytes($"{len:x}{sig}\r\n"));
            ms.Write(data, offset, len);
            ms.Write("\r\n"u8);
        }
        ms.Write(Encoding.ASCII.GetBytes($"0{sig}\r\n"));
        ms.Write(signedChunks ? "\r\n"u8 : "x-amz-checksum-crc32:AAAAAA==\r\n\r\n"u8);
        return ms.ToArray();
    }

    private static async Task<byte[]> DecodeAsync(MemoryStream framed, int readBufferSize)
    {
        await using var decoded = new AwsChunkedStream(framed);
        using var sink = new MemoryStream();

        var buffer = new byte[readBufferSize];
        int read;
        while ((read = await decoded.ReadAsync(buffer)) > 0)
            sink.Write(buffer, 0, read);

        return sink.ToArray();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ChunksLargerThanTheReadBuffer_DecodeToThePayload(bool signedChunks)
    {
        var payload = new byte[200 * 1024];
        new Random(7).NextBytes(payload);
        using var framed = new MemoryStream(Frame(payload, 100 * 1024, signedChunks));

        var decoded = await DecodeAsync(framed, readBufferSize: 4096);

        Assert.Equal(payload, decoded);
        Assert.Equal(framed.Length, framed.Position); // the trailer lines were consumed too
    }

    [Fact]
    public async Task SingleByteReads_DecodeToThePayload()
    {
        var payload = Encoding.UTF8.GetBytes("a body split across three chunks of framing");
        using var framed = new MemoryStream(Frame(payload, 16));

        var decoded = await DecodeAsync(framed, readBufferSize: 1);

        Assert.Equal(payload, decoded);
        Assert.Equal(framed.Length, framed.Position);
    }

    [Fact]
    public async Task EmptyBody_DecodesToNothing()
    {
        using var framed = new MemoryStream(Frame([], 16));

        Assert.Empty(await DecodeAsync(framed, readBufferSize: 4096));
    }

    [Theory]
    [InlineData("ffffffffff")] // wider than a chunk size can be
    [InlineData("ffffffff")]   // 4 GiB, above the 1 GiB cap
    [InlineData("zz")]         // not hex
    [InlineData("")]           // no size at all
    public async Task InvalidChunkSize_Throws(string hexSize)
    {
        using var framed = new MemoryStream(Encoding.ASCII.GetBytes($"{hexSize};chunk-signature=x\r\npayload\r\n"));

        await Assert.ThrowsAsync<InvalidDataException>(() => DecodeAsync(framed, readBufferSize: 4096));
    }

    [Fact]
    public async Task BodyThatEndsMidChunk_Throws()
    {
        using var framed = new MemoryStream("10\r\nfour bytes"u8.ToArray());

        await Assert.ThrowsAsync<InvalidDataException>(() => DecodeAsync(framed, readBufferSize: 4096));
    }
}
