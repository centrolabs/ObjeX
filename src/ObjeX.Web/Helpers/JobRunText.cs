using System.Text.Json;
using ObjeX.Core.Interfaces;
using ObjeX.Web.Components.Ui;

namespace ObjeX.Web.Helpers;

/// <summary>How the Jobs pages show a run: badge tone and label, durations, the actions a state allows, state details.</summary>
public static class JobRunText
{
    public static OxTone ToneOf(JobRunState state) => state switch
    {
        JobRunState.Succeeded => OxTone.Accent,
        JobRunState.Failed => OxTone.Danger,
        JobRunState.Retrying => OxTone.Warning,
        JobRunState.Other => OxTone.Muted,
        _ => OxTone.Default,
    };

    public static string LabelOf(JobRunState state) => state switch
    {
        JobRunState.Processing => "Running",
        _ => state.ToString(),
    };

    public static string FormatDuration(TimeSpan d) => d.TotalSeconds switch
    {
        < 1 => $"{d.TotalMilliseconds:0} ms",
        < 60 => $"{d.TotalSeconds:0.0} s",
        < 3600 => $"{(int)d.TotalMinutes} min {d.Seconds} s",
        _ => $"{(int)d.TotalHours} h {d.Minutes} min",
    };

    public static bool IsActive(JobRunState? state) => state is JobRunState.Queued or JobRunState.Processing;

    public static bool CanRetry(JobRunState state) => state is JobRunState.Failed or JobRunState.Retrying;

    public static bool CanDelete(JobRunState state) => state is not JobRunState.Processing and not JobRunState.Other;

    public static string DetailsUrl(string jobId) => $"/jobs/runs/{Uri.EscapeDataString(jobId)}";

    // The step's own time is the row's time, and result and stack trace have their own cards; the rest is shown as is.
    private static readonly (string Key, string Label, string Unit)[] Shown =
    [
        ("Queue", "Queue", ""), ("ServerId", "Server", ""), ("WorkerId", "Worker", ""),
        ("Latency", "Latency", " ms"), ("PerformanceDuration", "Duration", " ms"),
        ("ExceptionType", "Exception", ""), ("ExceptionMessage", "Message", ""),
    ];

    public static string StateDetails(JobStateChange change) => string.Join(" · ", Shown
        .Where(s => change.Data.TryGetValue(s.Key, out var value) && !string.IsNullOrEmpty(value))
        .Select(s => $"{s.Label} {change.Data[s.Key]}{s.Unit}"));

    /// <summary>The stored result JSON, indented and without Hangfire's "$type"; the text itself when it is not JSON.</summary>
    public static string Indented(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            object value = doc.RootElement.ValueKind == JsonValueKind.Object
                ? doc.RootElement.EnumerateObject().Where(p => p.Name != "$type").ToDictionary(p => p.Name, p => p.Value)
                : doc.RootElement;
            return JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (JsonException)
        {
            return raw;
        }
    }
}
