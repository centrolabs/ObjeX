namespace ObjeX.Core.Models;

public record ListObjectsResult(
    IEnumerable<BlobObject> Objects,
    IEnumerable<string> CommonPrefixes
);
