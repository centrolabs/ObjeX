namespace ObjeX.Api.Options;

/// <summary>
/// Buckets and one S3 credential to create on startup so integrations work without manual UI setup.
/// Everything is owned by the default admin. Empty values are no-ops; existing resources are skipped.
/// </summary>
public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    /// <summary>Comma-separated bucket names.</summary>
    public string? Buckets { get; set; }

    public SeedCredential S3Credential { get; set; } = new();

    public IEnumerable<string> BucketNames =>
        (Buckets ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public sealed class SeedCredential
    {
        public string? Name { get; set; }
        public string? AccessKeyId { get; set; }
        public string? SecretAccessKey { get; set; }

        public bool IsSet => !string.IsNullOrWhiteSpace(AccessKeyId) && !string.IsNullOrWhiteSpace(SecretAccessKey);
    }
}
