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
        var hasWildcard = term.Contains('*') || term.Contains('?');
        var pattern = term
            .Replace("\\", "\\\\")
            .Replace("%", "\\%")
            .Replace("_", "\\_")
            .Replace('*', '%')
            .Replace('?', '_');
        return hasWildcard ? $"%{pattern}" : $"%{pattern}%";
    }
}
