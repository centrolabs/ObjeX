namespace ObjeX.Api.Options;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Blob root. Relative paths resolve against the content root.</summary>
    public string BasePath { get; set; } = "data/blobs";

    /// <summary>Kestrel request body limit. Null = unlimited; the free-disk guard is the real protection.</summary>
    public long? MaxUploadBytes { get; set; }

    /// <summary>Uploads are rejected with 507 once free disk space would drop below this.</summary>
    public long MinimumFreeDiskBytes { get; set; } = 500L * 1024 * 1024;
}
