using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace ObjeX.Api.Options;

/// <summary>
/// Trust settings for X-Forwarded-For / X-Forwarded-Proto. Off by default: when enabled, only
/// the listed proxies and networks (plus loopback) may set the client IP and scheme.
/// The Host header is deliberately not forwarded-header-controlled: S3 clients sign it, so the
/// proxy must pass it through unchanged anyway.
/// </summary>
public sealed class ReverseProxyOptions
{
    public const string SectionName = "ReverseProxy";

    public bool Enabled { get; set; }

    /// <summary>Individual proxy IPs, e.g. "10.0.0.5".</summary>
    public string[] KnownProxies { get; set; } = [];

    /// <summary>Proxy networks in CIDR notation, e.g. "172.16.0.0/12".</summary>
    public string[] KnownNetworks { get; set; } = [];

    public void Apply(ForwardedHeadersOptions options)
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

        foreach (var raw in KnownProxies)
        {
            if (!IPAddress.TryParse(raw, out var ip))
                throw new InvalidOperationException($"ReverseProxy:KnownProxies contains '{raw}', which is not an IP address.");
            options.KnownProxies.Add(ip);
        }

        foreach (var raw in KnownNetworks)
        {
            if (!System.Net.IPNetwork.TryParse(raw, out var network))
                throw new InvalidOperationException($"ReverseProxy:KnownNetworks contains '{raw}', which is not a CIDR network (e.g. 172.16.0.0/12).");
            options.KnownIPNetworks.Add(network);
        }
    }
}
