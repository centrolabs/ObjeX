namespace ObjeX.Api.S3;

/// <summary>
/// Decodes the AWS chunked transfer encoding used when x-amz-content-sha256
/// starts with "STREAMING-". The wire format is:
///   {hex-size};chunk-signature={sig}\r\n{data}\r\n
///   ...
///   0;chunk-signature={sig}\r\n\r\n
/// This stream strips the framing and yields only the payload bytes.
/// </summary>
public sealed class AwsChunkedStream : Stream
{
    private readonly Stream _inner;
    private byte[]? _chunkBuffer;
    private int _chunkOffset;
    private int _chunkRemaining;
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

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (_done) return 0;

        while (_chunkRemaining == 0)
        {
            // Read chunk header line byte-by-byte until \n
            var headerLine = await ReadLineAsync(cancellationToken);
            if (headerLine is null) { _done = true; return 0; }

            // Parse chunk size from "hex-size" or "hex-size;chunk-signature=..."
            var semiIdx = headerLine.IndexOf(';');
            var hexPart = semiIdx >= 0 ? headerLine[..semiIdx] : headerLine;
            hexPart = hexPart.Trim();

            if (hexPart.Length == 0 || !int.TryParse(hexPart, System.Globalization.NumberStyles.HexNumber, null, out var chunkSize) || chunkSize == 0)
            {
                _done = true;
                return 0;
            }

            // Read the full chunk data
            _chunkBuffer = new byte[chunkSize];
            var totalRead = 0;
            while (totalRead < chunkSize)
            {
                var n = await _inner.ReadAsync(_chunkBuffer, totalRead, chunkSize - totalRead, cancellationToken);
                if (n == 0) { _done = true; return 0; }
                totalRead += n;
            }

            // Consume trailing \r\n after chunk data
            await SkipBytesAsync(2, cancellationToken);

            _chunkOffset = 0;
            _chunkRemaining = chunkSize;
        }

        var toCopy = Math.Min(count, _chunkRemaining);
        Buffer.BlockCopy(_chunkBuffer!, _chunkOffset, buffer, offset, toCopy);
        _chunkOffset += toCopy;
        _chunkRemaining -= toCopy;
        return toCopy;
    }

    private async Task<string?> ReadLineAsync(CancellationToken ct)
    {
        var bytes = new List<byte>(128);
        var buf = new byte[1];
        while (true)
        {
            var n = await _inner.ReadAsync(buf, 0, 1, ct);
            if (n == 0) return bytes.Count > 0 ? System.Text.Encoding.ASCII.GetString(bytes.ToArray()) : null;
            if (buf[0] == (byte)'\n')
                return System.Text.Encoding.ASCII.GetString(bytes.ToArray()).TrimEnd('\r');
            bytes.Add(buf[0]);
        }
    }

    private async Task SkipBytesAsync(int count, CancellationToken ct)
    {
        var buf = new byte[1];
        for (var i = 0; i < count; i++)
        {
            var n = await _inner.ReadAsync(buf, 0, 1, ct);
            if (n == 0) break;
        }
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
