namespace ObjeX.Web.Components.Ui;

public enum OxButtonVariant { Default, Primary, Danger }

/// <summary>Colour role of an icon, a badge, an alert or a meter. Default inherits the text colour.</summary>
public enum OxTone { Default, Muted, Accent, Warning, Danger }

public enum OxAlign { Stretch, Start, Center, End, Baseline }

public enum OxJustify { Start, Center, End, Between }

public enum OxTextVariant { Body, Muted, Small, Label, Title, Heading, Figure, Mono }

/// <summary>Fixed widths for a control inside <see cref="OxBox"/>: a number, a unit, a search field, a form.</summary>
public enum OxWidth { Auto, Xs, Sm, Md, Lg, Page, Fill }

/// <summary>Side of the anchor a tooltip prefers; it flips when the viewport has no room there.</summary>
public enum OxPlacement { Top, Bottom, Right, Left }

public enum OxPreviewKind { Image, Video, Audio, Pdf, Text }

/// <summary>What a file row shows: decides the icon and, for folders, images and videos, its colour.</summary>
public enum OxFileKind { File, Folder, Bucket, Image, Video, Audio, Text, Pdf, Archive }

/// <summary>One entry of <see cref="OxBreadcrumbs"/>. The last entry is the current location and needs no link.</summary>
public record OxCrumb(string Text, string? Href = null);

/// <summary>Durations a page waits on so motion in the Ui library can finish.</summary>
public static class OxMotion
{
    /// <summary>One turn of a spinning icon (ox-icon--spin). A busy state lasts at least this long, so a fast reload still shows that the click did something.</summary>
    public static readonly TimeSpan SpinTurn = TimeSpan.FromMilliseconds(800);
}

/// <summary>Sizes a page hands to Radzen parameters that take a CSS length, so no page invents its own.</summary>
public static class OxSizes
{
    public const string CheckColumn = "40px";
    public const string ActionsColumn = "112px";
    /// <summary>For a row whose only control is the menu.</summary>
    public const string MenuColumn = "56px";
    /// <summary>For a row whose action is one labelled button.</summary>
    public const string ButtonColumn = "140px";
    public const string TypeColumn = "180px";
    public const string SizeColumn = "110px";
    /// <summary>Fits "2026-09-16 10:54 AM" of the 12-hour clock.</summary>
    public const string DateColumn = "190px";
    public const string TimestampColumn = "215px";
    public const string ShortColumn = "140px";
    public const string NumberColumn = "110px";
    public const string NameColumn = "240px";
    /// <summary>Row class for a selected grid row. RowRender replaces the class attribute, so Radzen's own row class is repeated.</summary>
    public const string SelectedRowClass = "rz-data-row ox-row-selected";
    /// <summary>Grid class for rows whose cells stack two lines: the row grows instead of squeezing them.</summary>
    public const string RoomyGridClass = "ox-grid-roomy";
    /// <summary>Grid class for rows that open something on click: the pointer shows it.</summary>
    public const string ClickableGridClass = "ox-grid-clickable";
    /// <summary>Grid class for many fixed columns: a narrow screen scrolls sooner, so the name column keeps room.</summary>
    public const string WideGridClass = "ox-grid-wide";
    public const string DialogSmall = "440px";
    public const string DialogMedium = "480px";
    public const string DialogWide = "640px";
    public const string DialogLarge = "860px";
}
