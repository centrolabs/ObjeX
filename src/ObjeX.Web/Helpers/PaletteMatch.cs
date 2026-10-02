namespace ObjeX.Web.Helpers;

/// <summary>How the command palette ranks a label against what was typed, like Raycast or Alfred.</summary>
public static class PaletteMatch
{
    /// <summary>
    /// Higher is better; null means no match. A prefix beats the start of a later word, which beats any substring, which
    /// beats the letters in order with gaps ("dsh" finds "Dashboard"). Within a kind the shorter label wins. Case is ignored;
    /// an empty query matches everything with zero.
    /// </summary>
    public static int? Score(string label, string? query)
    {
        var q = query?.Trim().ToLowerInvariant() ?? string.Empty;
        if (q.Length == 0) return 0;
        var text = label.ToLowerInvariant();
        var shortness = 99 - Math.Min(text.Length, 99);

        if (text.StartsWith(q, StringComparison.Ordinal)) return 400 + shortness;
        var at = text.IndexOf(q, StringComparison.Ordinal);
        if (at > 0) return (char.IsLetterOrDigit(text[at - 1]) ? 200 : 300) + shortness;

        var from = 0;
        foreach (var c in q)
        {
            if (c == ' ') continue;
            from = text.IndexOf(c, from);
            if (from < 0) return null;
            from++;
        }
        return 100 + shortness;
    }
}
