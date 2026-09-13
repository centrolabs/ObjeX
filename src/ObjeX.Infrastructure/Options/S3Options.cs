namespace ObjeX.Infrastructure.Options;

/// <summary>S3 endpoint settings; in Infrastructure because Blazor pages need them and Web cannot reference Api.</summary>
public sealed class S3Options
{
    public const string SectionName = "S3";

    /// <summary>Base URL S3 clients reach this instance on; presigned URLs and Location headers are built from it.</summary>
    public string PublicUrl { get; set; } = "http://localhost:9000";
}
