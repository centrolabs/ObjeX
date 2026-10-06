using System.Text;

namespace ObjeX.Infrastructure.Metadata;

/// <summary>
/// Turns a search term into a LIKE pattern: <c>*</c> matches any run of characters, <c>?</c> exactly one,
/// and LIKE's own <c>%</c>, <c>_</c> and <c>\</c> stay literal. A term without a wildcard matches anywhere,
/// a term with one is anchored at the end so <c>*.pdf</c> excludes <c>a.pdfx</c>.
/// </summary>
internal static class SearchPattern
{
    public static string FromTerm(string term)
    {
        var pattern = Escape(term).Replace('*', '%').Replace('?', '_');
        return HasWildcard(term) ? $"%{pattern}" : $"%{pattern}%";
    }

    /// <summary>
    /// Keys are stored byte for byte, as S3 does, so "é" may be one code point (NFC) or "e" plus a combining
    /// accent (NFD, common from macOS). Matching the term in both forms finds either spelling; a term that reads
    /// the same in both forms gets one pattern, so the database tests each key once.
    /// </summary>
    public static IReadOnlyList<string> FromTermInBothForms(string term) => [.. BothForms(term).Select(FromTerm).Distinct()];

    /// <summary>Patterns for keys in which the term starts the key or a segment after a <c>/</c>. They rank, they do not filter; a wildcard term gets none.</summary>
    public static IReadOnlyList<string> SegmentStarts(string term) =>
        HasWildcard(term) ? [] : [.. BothForms(term).Distinct().SelectMany(form => new[] { $"{Escape(form)}%", $"%/{Escape(form)}%" })];

    private static bool HasWildcard(string term) => term.Contains('*') || term.Contains('?');

    private static string Escape(string term) => term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private static string[] BothForms(string term) => [term.Normalize(NormalizationForm.FormC), term.Normalize(NormalizationForm.FormD)];
}
