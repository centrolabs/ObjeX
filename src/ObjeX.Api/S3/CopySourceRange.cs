using System.Text.RegularExpressions;

namespace ObjeX.Api.S3;

/// <summary>The x-amz-copy-source-range of UploadPartCopy: one inclusive range, bytes=first-last, inside the source.</summary>
public static partial class CopySourceRange
{
    [GeneratedRegex(@"^bytes=(\d+)-(\d+)$")]
    private static partial Regex Pattern();

    public static IResult? TryParse(string? header, long sourceSize, out long first, out long length)
    {
        (first, length) = (0, sourceSize);
        if (string.IsNullOrEmpty(header))
            return null;

        var match = Pattern().Match(header);
        if (!match.Success || !long.TryParse(match.Groups[1].Value, out first) || !long.TryParse(match.Groups[2].Value, out var last))
            return S3Xml.Error(S3Errors.InvalidArgument, "The x-amz-copy-source-range value must be of the form bytes=first-last.");
        if (first > last || last >= sourceSize)
            return S3Xml.Error(S3Errors.InvalidRange, "The requested range is not satisfiable.");

        length = last - first + 1;
        return null;
    }
}
