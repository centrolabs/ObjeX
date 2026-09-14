using System.Text.Encodings.Web;
using System.Text.Json;

namespace ObjeX.Web.Helpers;

public static class TextPreview
{
    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // output goes into an escaped <pre>, so readable characters are safe
    };

    /// <summary>Types the preview dialog renders as escaped text; shared with the preview gate on the Objects page.</summary>
    public static bool IsTextLike(string contentType)
    {
        var mediaType = MediaType(contentType);
        return mediaType.StartsWith("text/", StringComparison.Ordinal)
            || mediaType is "application/json" or "application/xml" or "application/yaml" or "application/x-yaml"
                or "application/javascript" or "application/x-javascript"
            || mediaType.EndsWith("+json", StringComparison.Ordinal)
            || mediaType.EndsWith("+xml", StringComparison.Ordinal);
    }

    public static bool IsJson(string contentType)
    {
        var mediaType = MediaType(contentType);
        return mediaType is "application/json" or "text/json" || mediaType.EndsWith("+json", StringComparison.Ordinal);
    }

    /// <summary>Indents JSON for reading; anything that does not parse comes back unchanged.</summary>
    public static string PrettyPrintJson(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(document.RootElement, Indented);
        }
        catch (JsonException)
        {
            return json;
        }
    }

    private static string MediaType(string contentType) => contentType.Split(';')[0].Trim().ToLowerInvariant();
}
