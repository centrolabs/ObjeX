using ObjeX.Core.Interfaces;
using ObjeX.Web.Components.Ui;

namespace ObjeX.Web.Helpers;

/// <summary>The search index card in Settings in words: read only, because the configuration decides.</summary>
public static class SearchIndexText
{
    public const string HowToChange = "Read only. Set Search:TrigramIndex to true or false and restart ObjeX to change it.";

    public static string LabelOf(SearchIndexState state) => state switch
    {
        SearchIndexState.Ready => "On",
        SearchIndexState.Building => "Building",
        SearchIndexState.Off => "Off",
        SearchIndexState.Dropping => "Dropping",
        SearchIndexState.ExtensionMissing => "Extension missing",
        SearchIndexState.Missing => "Not built",
        _ => "Not used",
    };

    public static OxTone ToneOf(SearchIndexState state) => state switch
    {
        SearchIndexState.Ready => OxTone.Accent,
        SearchIndexState.ExtensionMissing or SearchIndexState.Missing => OxTone.Warning,
        SearchIndexState.Building or SearchIndexState.Dropping => OxTone.Default,
        _ => OxTone.Muted,
    };

    public static string Describe(SearchIndexStatus status) => status.State switch
    {
        SearchIndexState.Ready => $"Fast search over object keys. Takes {FileHelper.FormatBytes(status.SizeBytes ?? 0)} in the database.",
        SearchIndexState.Building => "ObjeX builds it in the background. Search works meanwhile, only slower.",
        SearchIndexState.Off => "Search scans every key: slower with many objects, no extra space in the database.",
        SearchIndexState.Dropping when status.SizeBytes is { } left => $"Switched off. ObjeX drops it; until then it takes {FileHelper.FormatBytes(left)} in the database.",
        SearchIndexState.Dropping => "Switched off. ObjeX drops it in the background.",
        SearchIndexState.ExtensionMissing => "The PostgreSQL extension pg_trgm is missing. A database administrator runs CREATE EXTENSION IF NOT EXISTS pg_trgm; ObjeX builds the index on its next start.",
        SearchIndexState.Missing => "ObjeX builds it on its next start. Until then search scans every key.",
        _ => "SQLite searches without an index.",
    };
}
