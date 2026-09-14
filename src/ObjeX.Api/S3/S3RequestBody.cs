namespace ObjeX.Api.S3;

/// <summary>SDKs frame streamed uploads as aws-chunked when they send a trailing checksum; every endpoint that writes a body unwraps it here.</summary>
public static class S3RequestBody
{
    public static bool IsAwsChunked(HttpRequest request) =>
        request.Headers.ContentEncoding.ToString().Contains("aws-chunked", StringComparison.OrdinalIgnoreCase)
        || request.Headers["x-amz-content-sha256"].ToString().StartsWith("STREAMING-", StringComparison.OrdinalIgnoreCase);

    public static Stream Decoded(HttpRequest request) =>
        IsAwsChunked(request) ? new AwsChunkedStream(request.Body) : request.Body;

    /// <summary>The payload size without chunk framing; Content-Length includes the framing when the body is aws-chunked.</summary>
    public static long? DecodedContentLength(HttpRequest request) =>
        long.TryParse(request.Headers["x-amz-decoded-content-length"], out var length) ? length : request.ContentLength;
}
