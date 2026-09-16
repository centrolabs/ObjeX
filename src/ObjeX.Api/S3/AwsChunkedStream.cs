using System.Globalization;
using System.Text;

namespace ObjeX.Api.S3;

/// <summary>
/// Decodes the AWS chunked transfer encoding used when x-amz-content-sha256
/// starts with "STREAMING-". The wire format is:
///   {hex-size};chunk-signature={sig}\r\n{data}\r\n
///   ...
///   0;chunk-signature={sig}\r\n\r\n
/// This stream strips the framing and yields only the payload bytes. Chunk data never lands in a
/// buffer of its own — a client's declared chunk size cannot drive an allocation here.
/// </summary>
public sealed class AwsChunkedStream : Stream
{
    private const long MaxChunkSize = 1L * 1024 * 1024 * 1024;

    private readonly Stream _inner;
    private readonly byte[] _lineBuffer = new byte[1024];
    private readonly byte[] _single = new byte[1];
    private long _remainingInChunk;
    private bool _inChunk;
    private bool _sawHeader;
    private bool _done;

    public AwsChunkedStream(Stream inner) => _inner = inner;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_done || buffer.IsEmpty) return 0;

        while (_remainingInChunk == 0)
        {
            if (_inChunk)
            {
                await SkipBytesAsync(2, cancellationToken); // CRLF after the chunk data
                _inChunk = false;
            }

            var size = await ReadChunkSizeAsync(cancellationToken);
            if (size <= 0)
            {
                if (size == 0) await SkipTrailerAsync(cancellationToken);
                _done = true;
                return 0;
            }

            _remainingInChunk = size;
            _inChunk = true;
        }

        var toRead = (int)Math.Min(_remainingInChunk, buffer.Length);
        var read = await _inner.ReadAsync(buffer[..toRead], cancellationToken);
        if (read == 0) throw new InvalidDataException("Truncated aws-chunked body: chunk data ended early.");

        _remainingInChunk -= read;
        return read;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        await ReadAsync(buffer.AsMemory(offset, count), cancellationToken);

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count), CancellationToken.None).AsTask().GetAwaiter().GetResult();

    /// <summary>The chunk size, 0 for the terminating chunk, -1 when the body carries no framing at all.</summary>
    private async Task<long> ReadChunkSizeAsync(CancellationToken ct)
    {
        var line = await ReadLineAsync(ct);
        if (line is null)
        {
            if (_sawHeader) throw new InvalidDataException("Truncated aws-chunked body: no terminating chunk.");
            return -1;
        }

        var semicolon = line.IndexOf(';');
        var hex = (semicolon >= 0 ? line[..semicolon] : line).Trim();

        // Eight hex digits are the widest a chunk size can be; anything longer overflows before the cap check.
        if (hex.Length is 0 or > 8
            || !long.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var size)
            || size > MaxChunkSize)
        {
            throw new InvalidDataException($"Invalid aws-chunked chunk size '{hex}'.");
        }

        _sawHeader = true;
        return size;
    }

    /// <summary>Consumes the trailer lines after the terminating chunk. Trailer checksums stay unverified.</summary>
    private async Task SkipTrailerAsync(CancellationToken ct)
    {
        while (await ReadLineAsync(ct) is { Length: > 0 }) { }
    }

    private async Task<string?> ReadLineAsync(CancellationToken ct)
    {
        var length = 0;
        while (true)
        {
            var n = await _inner.ReadAsync(_single.AsMemory(0, 1), ct);
            if (n == 0)
                return length > 0 ? Encoding.ASCII.GetString(_lineBuffer, 0, length).TrimEnd('\r') : null;
            if (_single[0] == (byte)'\n')
                return Encoding.ASCII.GetString(_lineBuffer, 0, length).TrimEnd('\r');
            if (length == _lineBuffer.Length)
                throw new InvalidDataException("Invalid aws-chunked framing: header line too long.");
            _lineBuffer[length++] = _single[0];
        }
    }

    private async Task SkipBytesAsync(int count, CancellationToken ct)
    {
        for (var i = 0; i < count; i++)
        {
            var n = await _inner.ReadAsync(_single.AsMemory(0, 1), ct);
            if (n == 0) break;
        }
    }

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
