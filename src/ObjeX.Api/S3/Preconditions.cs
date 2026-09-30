using System.Globalization;

using ObjeX.Core.Models;

namespace ObjeX.Api.S3;

/// <summary>Conditional writes: If-Match / If-None-Match on PUT and complete, x-amz-copy-source-if-* on copy.</summary>
public static class Preconditions
{
    public static bool HasWriteConditions(HttpRequest request) =>
        request.Headers.IfMatch.Count > 0 || request.Headers.IfNoneMatch.Count > 0;

    /// <param name="existing">The object currently under the key, or null.</param>
    public static IResult? CheckWrite(HttpRequest request, BlobObject? existing)
    {
        var ifMatch = request.Headers.IfMatch.ToString();
        if (ifMatch.Length > 0)
        {
            if (existing is null)
                return S3Xml.Error(S3Errors.NoSuchKey, "The specified key does not exist.", StatusCodes.Status404NotFound);
            if (!Matches(ifMatch, existing.ETag))
                return Failed();
        }

        var ifNoneMatch = request.Headers.IfNoneMatch.ToString();
        return ifNoneMatch.Length > 0 && existing is not null && Matches(ifNoneMatch, existing.ETag) ? Failed() : null;
    }

    public static IResult? CheckCopySource(HttpRequest request, BlobObject source)
    {
        var ifMatch = request.Headers["x-amz-copy-source-if-match"].ToString();
        var ifNoneMatch = request.Headers["x-amz-copy-source-if-none-match"].ToString();
        // HTTP dates carry whole seconds, so the comparison does too.
        var lastModified = DateTime.SpecifyKind(source.UpdatedAt.AddTicks(-(source.UpdatedAt.Ticks % TimeSpan.TicksPerSecond)), DateTimeKind.Utc);

        // A matching If-Match overrides If-Unmodified-Since, a non-matching If-None-Match overrides If-Modified-Since, as on AWS.
        if (ifMatch.Length > 0 && !Matches(ifMatch, source.ETag))
            return Failed();
        if (ifNoneMatch.Length > 0 && Matches(ifNoneMatch, source.ETag))
            return Failed();
        if (ifMatch.Length == 0 && Date(request, "x-amz-copy-source-if-unmodified-since") is { } unmodifiedSince && lastModified > unmodifiedSince)
            return Failed();
        if (ifNoneMatch.Length == 0 && Date(request, "x-amz-copy-source-if-modified-since") is { } modifiedSince && lastModified <= modifiedSince)
            return Failed();
        return null;
    }

    private static bool Matches(string header, string etag) =>
        header.Split(',').Select(v => v.Trim().Trim('"')).Any(v => v == "*" || v == etag);

    private static DateTime? Date(HttpRequest request, string header) =>
        DateTimeOffset.TryParse(request.Headers[header], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value)
            ? value.UtcDateTime
            : null;

    private static IResult Failed() =>
        S3Xml.Error(S3Errors.PreconditionFailed, "At least one of the preconditions you specified did not hold.", StatusCodes.Status412PreconditionFailed);
}
