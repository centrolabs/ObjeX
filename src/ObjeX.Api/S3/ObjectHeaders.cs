using System.Text.Json;

namespace ObjeX.Api.S3;

/// <summary>
/// The per-object headers S3 stores on upload and returns on GET and HEAD: x-amz-meta-* plus a few system
/// headers. They live together as one JSON object in BlobObject.CustomMetadata.
/// </summary>
public static class ObjectHeaders
{
    private const string MetaPrefix = "x-amz-meta-";

    private static readonly string[] System = ["cache-control", "content-disposition", "content-encoding", "content-language", "expires"];

    public static string? Extract(IHeaderDictionary headers)
    {
        var stored = new Dictionary<string, string>();
        foreach (var (name, value) in headers)
        {
            var key = name.ToLowerInvariant();
            if (key == "content-encoding")
            {
                // aws-chunked describes the transfer, not the object.
                var encoding = string.Join(", ", value.ToString().Split(',').Select(e => e.Trim())
                    .Where(e => e.Length > 0 && !e.Equals("aws-chunked", StringComparison.OrdinalIgnoreCase)));
                if (encoding.Length > 0)
                    stored[key] = encoding;
            }
            else if (key.StartsWith(MetaPrefix, StringComparison.Ordinal) || System.Contains(key))
                stored[key] = value.ToString();
        }
        return stored.Count > 0 ? JsonSerializer.Serialize(stored) : null;
    }

    public static void Apply(HttpResponse response, string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return;
        var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(stored);
        if (headers is null) return;
        foreach (var (key, value) in headers)
            if (key.StartsWith(MetaPrefix, StringComparison.OrdinalIgnoreCase) || System.Contains(key))
                response.Headers[key] = value;
    }

    /// <summary>Applies the response-* query parameters of a GET; returns the overridden Content-Type, if any.</summary>
    public static string? ApplyOverrides(HttpRequest request, HttpResponse response)
    {
        foreach (var header in System)
            if (request.Query.TryGetValue($"response-{header}", out var value))
                response.Headers[header] = value.ToString();
        return request.Query.TryGetValue("response-content-type", out var type) ? type.ToString() : null;
    }
}
