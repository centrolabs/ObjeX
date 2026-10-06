namespace ObjeX.Api.Options;

public sealed class LogOptions
{
    public const string SectionName = "Log";

    /// <summary>Daily log file, compact JSON, 30 days kept. Relative paths resolve against the content root. Empty = console only.</summary>
    public string? FilePath { get; set; } = "data/logs/objex-.log";
}
