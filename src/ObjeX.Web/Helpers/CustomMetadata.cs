using System.Text.Json;

namespace ObjeX.Web.Helpers;

public static class CustomMetadata
{
    private const string Prefix = "x-amz-meta-";

    /// <summary>Reads the stored JSON dictionary of x-amz-meta-* headers; anything unparsable comes back empty.</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return [];

            return [.. document.RootElement.EnumerateObject()
                .Select(p => new KeyValuePair<string, string>(StripPrefix(p.Name), Value(p.Value)))
                .OrderBy(p => p.Key, StringComparer.Ordinal)];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string StripPrefix(string name) =>
        name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) ? name[Prefix.Length..] : name;

    private static string Value(JsonElement element) =>
        element.ValueKind == JsonValueKind.String ? element.GetString() ?? string.Empty : element.GetRawText();
}
