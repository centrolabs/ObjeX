namespace ObjeX.Api.Options;

public sealed class MetricsOptions
{
    public const string SectionName = "Metrics";

    public bool Enabled { get; set; }

    /// <summary>When set, /metrics requires this value as a Bearer token (Prometheus: bearer_token).</summary>
    public string? Token { get; set; }
}
