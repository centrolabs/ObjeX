namespace ObjeX.Web.Components.Ui;

public enum OxButtonVariant { Default, Primary, Danger }

/// <summary>Colour role of an icon, a badge, an alert or a meter. Default inherits the text colour.</summary>
public enum OxTone { Default, Muted, Accent, Warning, Danger }

public enum OxAlign { Stretch, Start, Center, End, Baseline }

public enum OxJustify { Start, Center, End, Between }

public enum OxTextVariant { Body, Muted, Small, Label, Title, Heading, Figure, Mono }

/// <summary>What a file row shows: decides the icon and, for folders, images and videos, its colour.</summary>
public enum OxFileKind { File, Folder, Image, Video, Audio, Text, Pdf, Archive }

/// <summary>One entry of <see cref="OxBreadcrumbs"/>. The last entry is the current location and needs no link.</summary>
public record OxCrumb(string Text, string? Href = null);

/// <summary>Sizes a page hands to Radzen parameters that take a CSS length, so no page invents its own.</summary>
public static class OxSizes
{
    public const string CheckColumn = "40px";
    public const string ActionsColumn = "112px";
    public const string DialogSmall = "440px";
    public const string DialogMedium = "480px";
    public const string DialogLarge = "860px";
}
