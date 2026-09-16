using System.Globalization;

namespace ObjeX.Web.Services;

/// <summary>The browser's zone for the current circuit, set from the objex-tz cookie by Routes.</summary>
public sealed class BrowserTimeZone
{
    const int MaxIdLength = 64;
    const string Dash = "—";

    public TimeZoneInfo Zone { get; private set; } = TimeZoneInfo.Utc;

    public void Set(string? id)
    {
        Zone = id is { Length: > 0 and <= MaxIdLength } && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone)
            ? zone
            : TimeZoneInfo.Utc;
    }

    // Stored timestamps come back from the database with Kind Unspecified; say out loud that they are UTC.
    public DateTime ToLocal(DateTime utc)
        => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);

    public DateTime ToLocal(DateTimeOffset utc)
        => TimeZoneInfo.ConvertTimeFromUtc(utc.UtcDateTime, Zone);

    public string Format(DateTime utc) => ToLocal(utc).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    public string Format(DateTime? utc) => utc is null ? Dash : Format(utc.Value);

    public string Format(DateTimeOffset utc) => ToLocal(utc).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    public string Format(DateTimeOffset? utc) => utc is null ? Dash : Format(utc.Value);

    public string FormatSeconds(DateTime utc) => ToLocal(utc).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
}
