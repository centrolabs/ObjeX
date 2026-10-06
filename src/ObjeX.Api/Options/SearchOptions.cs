namespace ObjeX.Api.Options;

public sealed class SearchOptions
{
    public const string SectionName = "Search";

    /// <summary>
    /// PostgreSQL only: a trigram index on the object keys, about 40 to 200 MB per million objects. Search finds the
    /// same keys without it, only slower. False drops the index at the next start.
    /// </summary>
    public bool TrigramIndex { get; set; } = true;
}
