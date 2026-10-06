using System.Text;

namespace ObjeX.Infrastructure.Metadata;

/// <summary>
/// Turns a search term into LIKE patterns. The term splits into words at whitespace, and a key matches when it contains
/// every word, in any order. In a word, <c>*</c> matches any run of characters, <c>?</c> exactly one, and LIKE's own
/// <c>%</c>, <c>_</c> and <c>\</c> stay literal. A word without a wildcard matches anywhere, a word with one is anchored
/// at the end so <c>*.pdf</c> excludes <c>a.pdfx</c>.
/// </summary>
internal static class SearchPattern
{
    public static IReadOnlyList<string> Words(string term) =>
        [.. term.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal)];

    public static string FromWord(string word)
    {
        var pattern = Escape(word).Replace('*', '%').Replace('?', '_');
        return HasWildcard(word) ? $"%{pattern}" : $"%{pattern}%";
    }

    /// <summary>
    /// Keys are stored byte for byte, as S3 does, so "é" may be one code point (NFC) or "e" plus a combining
    /// accent (NFD, common from macOS). Matching the word in both forms finds either spelling; a word that reads
    /// the same in both forms gets one pattern, so the database tests each key once.
    /// </summary>
    public static IReadOnlyList<string> FromWordInBothForms(string word) => [.. BothForms(word).Select(FromWord).Distinct()];

    /// <summary>Patterns for keys in which a word of the term starts the key or a segment after a <c>/</c>. They rank, they do not filter; a wildcard word adds none.</summary>
    public static IReadOnlyList<string> SegmentStarts(string term) =>
        [.. Words(term).Where(word => !HasWildcard(word)).SelectMany(BothForms).Distinct()
            .SelectMany(form => new[] { $"{Escape(form)}%", $"%/{Escape(form)}%" })];

    private static bool HasWildcard(string word) => word.Contains('*') || word.Contains('?');

    private static string Escape(string word) => word.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private static string[] BothForms(string word) => [word.Normalize(NormalizationForm.FormC), word.Normalize(NormalizationForm.FormD)];
}
