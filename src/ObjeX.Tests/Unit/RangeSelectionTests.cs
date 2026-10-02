using ObjeX.Web.Helpers;

namespace ObjeX.Tests.Unit;

public class RangeSelectionTests
{
    static readonly string[] Rows = ["a", "b", "c", "d", "e"];

    [Fact]
    public void APlainClick_FlipsOneRow()
    {
        var selected = new HashSet<string>();
        RangeSelection.Apply(Rows, selected, null, "b", shift: false);
        Assert.Equal(["b"], selected);
        RangeSelection.Apply(Rows, selected, "b", "b", shift: false);
        Assert.Empty(selected);
    }

    [Fact]
    public void ShiftClick_SelectsEverythingBetween_InEitherDirection()
    {
        var selected = new HashSet<string> { "d" };
        RangeSelection.Apply(Rows, selected, "d", "b", shift: true);
        Assert.Equal(["b", "c", "d"], selected.Order());
    }

    [Fact]
    public void ShiftClick_OnASelectedRow_ClearsTheRange()
    {
        var selected = new HashSet<string>(Rows);
        RangeSelection.Apply(Rows, selected, "a", "c", shift: true);
        Assert.Equal(["d", "e"], selected.Order());
    }

    // The anchor may be gone after a folder change or a search; the click then counts alone.
    [Fact]
    public void ShiftClick_WithoutAVisibleAnchor_FlipsOneRow()
    {
        var selected = new HashSet<string>();
        var anchor = RangeSelection.Apply(Rows, selected, "zzz", "c", shift: true);
        Assert.Equal(["c"], selected);
        Assert.Equal("c", anchor);
    }
}
