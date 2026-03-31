using System.ComponentModel.DataAnnotations;

using ObjeX.Core.Interfaces;

namespace ObjeX.Core.Models;

public class BlobObject : IHasTimestamps
{
    // Properties
    [Required][Key] public Guid Id { get; init; } = Guid.NewGuid();
    [Required] public required string BucketName { get; set; }
    [Required] public required string Key { get; set; }
    public long Size { get; set; }
    public string ContentType { get; set; } = "application/octet-stream";
    public required string ETag { get; set; }
    public string? StoragePath { get; set; }
    public string? CustomMetadata { get; set; } // JSON dict of x-amz-meta-* headers

    // Navigation Properties
    public Bucket? Bucket { get; set; }

    // IHasTimestamps
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}