namespace ObjeX.Api.Options;

/// <summary>
/// Listening ports. Each port is its own API surface with its own pipeline: the UI port serves
/// Blazor plus the cookie-authenticated internal endpoints, the S3 port serves the SigV4-authenticated
/// S3 API. Which pipeline handles a request is decided by the port it arrived on, never by the Host header.
/// </summary>
public sealed class ServerOptions
{
    public const string SectionName = "Server";

    public int UiPort { get; set; } = 9001;
    public int S3Port { get; set; } = 9000;

    public void Validate()
    {
        if (UiPort is < 1 or > 65535)
            throw new InvalidOperationException($"Server:UiPort must be between 1 and 65535 (got {UiPort}).");
        if (S3Port is < 1 or > 65535)
            throw new InvalidOperationException($"Server:S3Port must be between 1 and 65535 (got {S3Port}).");
        if (UiPort == S3Port)
            throw new InvalidOperationException(
                $"Server:UiPort and Server:S3Port must differ (both are {UiPort}). The port decides which pipeline handles a request.");
    }
}
