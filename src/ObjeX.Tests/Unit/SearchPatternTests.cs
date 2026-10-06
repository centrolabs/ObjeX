using ObjeX.Infrastructure.Metadata;

namespace ObjeX.Tests.Unit;

public class SearchPatternTests
{
    [Fact]
    public void WordWithoutWildcard_MatchesAnywhere()
    {
        Assert.Equal("%report%", SearchPattern.FromWord("report"));
    }

    [Fact]
    public void LikeWildcardsInTheWordAreEscaped()
    {
        Assert.Equal(@"%100\%%", SearchPattern.FromWord("100%"));
        Assert.Equal(@"%a\_b%", SearchPattern.FromWord("a_b"));
        Assert.Equal(@"%back\\s%", SearchPattern.FromWord(@"back\s"));
    }

    [Fact]
    public void Star_BecomesPercentAndAnchorsAtTheEnd()
    {
        Assert.Equal("%%.pdf", SearchPattern.FromWord("*.pdf"));
        Assert.Equal("%report%", SearchPattern.FromWord("report*"));
    }

    [Fact]
    public void QuestionMark_BecomesUnderscore()
    {
        Assert.Equal(@"%img\_____.jpg", SearchPattern.FromWord("img_????.jpg"));
    }

    [Fact]
    public void WildcardAndLiteralPercentCombine()
    {
        Assert.Equal(@"%100\%%.txt", SearchPattern.FromWord("100%*.txt"));
    }

    [Fact]
    public void EmptyWord_MatchesEverything()
    {
        Assert.Equal("%%", SearchPattern.FromWord(""));
    }

    [Fact]
    public void BothForms_AreTheSameWhateverFormTheWordArrivesIn()
    {
        Assert.Equal(["%caf\u00e9%", "%cafe\u0301%"], SearchPattern.FromWordInBothForms("caf\u00e9"));
        Assert.Equal(["%caf\u00e9%", "%cafe\u0301%"], SearchPattern.FromWordInBothForms("cafe\u0301"));
    }

    [Fact]
    public void BothForms_GiveOnePatternWhenTheyAgree()
    {
        Assert.Equal(["%report%"], SearchPattern.FromWordInBothForms("report"));
    }

    [Fact]
    public void SegmentStarts_CoverTheKeyStartAndEverySegmentInBothForms()
    {
        Assert.Equal(["inv%", "%/inv%"], SearchPattern.SegmentStarts("inv"));
        Assert.Equal([@"a\_b%", @"%/a\_b%"], SearchPattern.SegmentStarts("a_b"));
        Assert.Equal(["caf\u00e9%", "%/caf\u00e9%", "cafe\u0301%", "%/cafe\u0301%"], SearchPattern.SegmentStarts("caf\u00e9"));
    }

    [Fact]
    public void SegmentStarts_CoverEveryWordButNoWildcardWord()
    {
        Assert.Equal(["inv%", "%/inv%", "mar%", "%/mar%"], SearchPattern.SegmentStarts("inv mar"));
        Assert.Equal(["inv%", "%/inv%"], SearchPattern.SegmentStarts("*.pdf inv img_????"));
        Assert.Empty(SearchPattern.SegmentStarts("*.pdf"));
    }

    [Fact]
    public void Words_SplitAtAnyWhitespaceAndDropRepeats()
    {
        Assert.Equal(["invoice", "march"], SearchPattern.Words("  invoice \t march\u00a0invoice "));
        Assert.Equal(["a_b"], SearchPattern.Words("a_b"));
        Assert.Empty(SearchPattern.Words("   "));
    }
}
