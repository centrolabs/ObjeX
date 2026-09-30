using System.Text.Json;

namespace ObjeX.Web.Helpers;

public static class CustomMetadata
{
    private const string Prefix = "x-amz-meta-";

    /// <summary>Reads the x-amz-meta-* entries of the stored header JSON, which also holds system headers; anything unparsable comes back empty.</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return [];

            return [.. document.RootElement.EnumerateObject()
                .Where(p => p.Name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
                .Select(p => new KeyValuePair<string, string>(p.Name[Prefix.Length..], Value(p.Value)))
                .OrderBy(p => p.Key, StringComparer.Ordinal)];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string Value(JsonElement element) =>
        element.ValueKind == JsonValueKind.String ? element.GetString() ?? string.Empty : element.GetRawText();
}
