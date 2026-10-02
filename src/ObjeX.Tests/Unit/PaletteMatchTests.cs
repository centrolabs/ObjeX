using ObjeX.Web.Helpers;

namespace ObjeX.Tests.Unit;

public class PaletteMatchTests
{
    [Fact]
    public void Prefix_BeatsWordStart_BeatsSubstring_BeatsLettersInOrder()
    {
        var prefix = PaletteMatch.Score("Buckets", "buck");
        var wordStart = PaletteMatch.Score("New bucket", "buck");
        var substring = PaletteMatch.Score("mybucket", "buck");
        var letters = PaletteMatch.Score("Big ruck", "buck");

        Assert.True(prefix > wordStart && wordStart > substring && substring > letters, $"{prefix} {wordStart} {substring} {letters}");
    }

    [Fact]
    public void LettersInOrder_FindTheLabel_ButNotOutOfOrder()
    {
        Assert.NotNull(PaletteMatch.Score("Dashboard", "dsh"));
        Assert.Null(PaletteMatch.Score("Dashboard", "hsd"));
        Assert.Null(PaletteMatch.Score("Jobs", "x"));
    }

    [Fact]
    public void ShorterLabel_WinsWithinAKind_AndCaseIsIgnored()
    {
        Assert.True(PaletteMatch.Score("photos", "PHO") > PaletteMatch.Score("photos-archive", "pho"));
    }

    [Fact]
    public void EmptyQuery_MatchesEverything() => Assert.Equal(0, PaletteMatch.Score("Settings", "  "));
}
