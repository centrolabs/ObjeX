using ObjeX.Infrastructure.Metadata;

namespace ObjeX.Tests.Unit;

public class SearchPatternTests
{
    [Fact]
    public void TermWithoutWildcard_MatchesAnywhere()
    {
        Assert.Equal("%report%", SearchPattern.FromTerm("report"));
    }

    [Fact]
    public void LikeWildcardsInTheTermAreEscaped()
    {
        Assert.Equal(@"%100\%%", SearchPattern.FromTerm("100%"));
        Assert.Equal(@"%a\_b%", SearchPattern.FromTerm("a_b"));
        Assert.Equal(@"%back\\s%", SearchPattern.FromTerm(@"back\s"));
    }

    [Fact]
    public void Star_BecomesPercentAndAnchorsAtTheEnd()
    {
        Assert.Equal("%%.pdf", SearchPattern.FromTerm("*.pdf"));
        Assert.Equal("%report%", SearchPattern.FromTerm("report*"));
    }

    [Fact]
    public void QuestionMark_BecomesUnderscore()
    {
        Assert.Equal(@"%img\_____.jpg", SearchPattern.FromTerm("img_????.jpg"));
    }

    [Fact]
    public void WildcardAndLiteralPercentCombine()
    {
        Assert.Equal(@"%100\%%.txt", SearchPattern.FromTerm("100%*.txt"));
    }

    [Fact]
    public void EmptyTerm_MatchesEverything()
    {
        Assert.Equal("%%", SearchPattern.FromTerm(""));
    }
}
