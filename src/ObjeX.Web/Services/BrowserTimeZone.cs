using System.Globalization;

namespace ObjeX.Web.Services;

/// <summary>The browser's zone for the current circuit, set from the objex-tz cookie by Routes.</summary>
public sealed class BrowserTimeZone
{
    const int MaxIdLength = 64;
    const string Dash = "—";

    public TimeZoneInfo Zone { get; private set; } = TimeZoneInfo.Utc;

    /// <summary>12-hour clock with AM and PM instead of 24 hours; set from UiPreferences.</summary>
    public bool Hour12 { get; set; }

    string Minutes => Hour12 ? "yyyy-MM-dd h:mm tt" : "yyyy-MM-dd HH:mm";
    string Seconds => Hour12 ? "yyyy-MM-dd h:mm:ss tt" : "yyyy-MM-dd HH:mm:ss";

    /// <summary>The browser's zone when this server does not know it; Zone is UTC then.</summary>
    public string? UnknownId { get; private set; }

    public void Set(string? id)
    {
        if (id is { Length: > 0 and <= MaxIdLength } && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone))
        {
            Zone = zone;
            UnknownId = null;
        }
        else
        {
            Zone = TimeZoneInfo.Utc;
            UnknownId = string.IsNullOrEmpty(id) ? null : id;
        }
    }

    // Stored timestamps come back from the database with Kind Unspecified; say out loud that they are UTC.
    public DateTime ToLocal(DateTime utc)
        => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);

    public DateTime ToLocal(DateTimeOffset utc)
        => TimeZoneInfo.ConvertTimeFromUtc(utc.UtcDateTime, Zone);

    public string Format(DateTime utc) => ToLocal(utc).ToString(Minutes, CultureInfo.InvariantCulture);

    public string Format(DateTime? utc) => utc is null ? Dash : Format(utc.Value);

    public string Format(DateTimeOffset utc) => ToLocal(utc).ToString(Minutes, CultureInfo.InvariantCulture);

    public string Format(DateTimeOffset? utc) => utc is null ? Dash : Format(utc.Value);

    public string FormatSeconds(DateTime utc) => ToLocal(utc).ToString(Seconds, CultureInfo.InvariantCulture);

    /// <summary>A time of day that is already local, for example from a schedule: 05:00 or 5:00 AM.</summary>
    public string FormatClock(TimeOnly time) => time.ToString(Hour12 ? "h:mm tt" : "HH:mm", CultureInfo.InvariantCulture);
}
