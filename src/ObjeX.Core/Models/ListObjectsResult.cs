namespace ObjeX.Core.Models;

/// <param name="NextMarker">Last key or common prefix of a truncated page; the next page starts after it.</param>
public record ListObjectsResult(
    IEnumerable<BlobObject> Objects,
    IEnumerable<string> CommonPrefixes,
    bool IsTruncated = false,
    string? NextMarker = null
);
