namespace ObjeX.Web.Helpers;

/// <summary>Checkbox clicks in a list, with Shift for a range, like a file manager.</summary>
public static class RangeSelection
{
    /// <summary>
    /// Applies a click on <paramref name="clicked"/>: it flips that row; with Shift every visible row between the last clicked
    /// row and this one takes the clicked row's new state. <paramref name="visible"/> is the order on screen, so a sorted list
    /// selects what the user sees between the two rows. Returns the anchor for the next Shift-click.
    /// </summary>
    public static T Apply<T>(IReadOnlyList<T> visible, ISet<T> selected, T? anchor, T clicked, bool shift)
    {
        var select = !selected.Contains(clicked);
        var from = anchor is null ? -1 : IndexOf(visible, anchor);
        var to = IndexOf(visible, clicked);

        if (shift && from >= 0 && to >= 0)
        {
            for (var i = Math.Min(from, to); i <= Math.Max(from, to); i++)
                Set(selected, visible[i], select);
        }
        else
        {
            Set(selected, clicked, select);
        }
        return clicked;
    }

    static int IndexOf<T>(IReadOnlyList<T> list, T item)
    {
        for (var i = 0; i < list.Count; i++)
            if (EqualityComparer<T>.Default.Equals(list[i], item)) return i;
        return -1;
    }

    static void Set<T>(ISet<T> selected, T item, bool select)
    {
        if (select) selected.Add(item);
        else selected.Remove(item);
    }
}
