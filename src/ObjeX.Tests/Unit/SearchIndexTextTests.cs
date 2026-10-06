using ObjeX.Core.Interfaces;
using ObjeX.Web.Components.Ui;
using ObjeX.Web.Helpers;

namespace ObjeX.Tests.Unit;

public class SearchIndexTextTests
{
    [Theory]
    [InlineData(SearchIndexState.Ready, "On", OxTone.Accent)]
    [InlineData(SearchIndexState.Building, "Building", OxTone.Default)]
    [InlineData(SearchIndexState.Off, "Off", OxTone.Muted)]
    [InlineData(SearchIndexState.Dropping, "Dropping", OxTone.Default)]
    [InlineData(SearchIndexState.ExtensionMissing, "Extension missing", OxTone.Warning)]
    [InlineData(SearchIndexState.Missing, "Not built", OxTone.Warning)]
    [InlineData(SearchIndexState.NotUsed, "Not used", OxTone.Muted)]
    public void EveryStateHasALabelAndATone(SearchIndexState state, string label, OxTone tone)
    {
        Assert.Equal(label, SearchIndexText.LabelOf(state));
        Assert.Equal(tone, SearchIndexText.ToneOf(state));
    }

    [Fact]
    public void Ready_NamesTheSizeInTheDatabase()
    {
        Assert.Contains("98.0 MB", SearchIndexText.Describe(new SearchIndexStatus(SearchIndexState.Ready, 98L * 1024 * 1024)));
    }

    [Fact]
    public void Dropping_NamesTheSpaceStillTaken()
    {
        Assert.Contains("98.0 MB", SearchIndexText.Describe(new SearchIndexStatus(SearchIndexState.Dropping, 98L * 1024 * 1024)));
        Assert.DoesNotContain("MB", SearchIndexText.Describe(new SearchIndexStatus(SearchIndexState.Dropping)));
    }

    [Fact]
    public void ExtensionMissing_NamesTheStatementToRun()
    {
        Assert.Contains("CREATE EXTENSION IF NOT EXISTS pg_trgm", SearchIndexText.Describe(new SearchIndexStatus(SearchIndexState.ExtensionMissing)));
    }

    [Fact]
    public void HowToChange_NamesTheConfigurationKey()
    {
        Assert.Contains("Search:TrigramIndex", SearchIndexText.HowToChange);
    }
}
