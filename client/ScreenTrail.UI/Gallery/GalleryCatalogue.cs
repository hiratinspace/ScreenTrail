namespace ScreenTrail.UI.Gallery;

/// <summary>The gallery matrix's columns (review finding B-56): each state a control can be drawn in.</summary>
internal enum GalleryState
{
    Rest,
    Hover,
    Pressed,
    Focused,
    Disabled,
    Loading,
    Error,
}

/// <summary>What a matrix row's sample is built as.</summary>
internal enum GallerySample
{
    Button,
    IconButton,
    Input,
    Placeholder,
    ComboBox,
    CheckBox,
    RadioButton,
    Segment,
    CheckChip,
    Slider,
}

/// <summary>One row of the matrix: a style, its sample's words, and the states it has.</summary>
internal sealed record GalleryRow(string Label, GallerySample Sample, string? StyleKey, string Text, bool IsChecked, IReadOnlyList<GalleryState> States);

/// <summary>
/// The component gallery as a state matrix (design system 10, review finding B-56): one row per control
/// style, one column per state, so the gallery is the component reference and every hover, pressed,
/// focused, disabled, loading and error look is in its renders. The tones of the badges and banners follow.
/// Plain data, so the Mac tests hold it against Components.xaml.
/// </summary>
internal static class GalleryCatalogue
{
    private static readonly GalleryState[] Pressable = [GalleryState.Rest, GalleryState.Hover, GalleryState.Pressed, GalleryState.Focused, GalleryState.Disabled];
    private static readonly GalleryState[] Field = [GalleryState.Rest, GalleryState.Hover, GalleryState.Focused, GalleryState.Disabled, GalleryState.Error];
    private static readonly GalleryState[] Hoverable = [GalleryState.Rest, GalleryState.Hover, GalleryState.Focused, GalleryState.Disabled];

    public static IReadOnlyList<GalleryState> Columns { get; } = Enum.GetValues<GalleryState>();

    public static IReadOnlyList<GalleryRow> Rows { get; } =
    [
        new("Primary", GallerySample.Button, "Button.Primary", "Publish", false, [.. Pressable, GalleryState.Loading]),
        new("Secondary", GallerySample.Button, "Button.Secondary", "Retry", false, Pressable),
        new("Ghost", GallerySample.Button, "Button.Ghost", "Skip for now", false, Pressable),
        new("Danger", GallerySample.Button, "Button.Danger", "Discard", false, Pressable),
        new("Danger solid", GallerySample.Button, "Button.DangerSolid", "Discard session", false, Pressable),
        new("Icon", GallerySample.IconButton, "Button.Icon", "Settings", false, Pressable),
        new("Link", GallerySample.Button, "Button.Link", "Open ticket", false, Pressable),
        new("Field", GallerySample.Input, "Input.Default", "#48213 · Printer offline", false, Field),
        new("Field, empty", GallerySample.Placeholder, "Input.Default", "For example keepass", false, [GalleryState.Rest, GalleryState.Hover, GalleryState.Focused]),
        new("Field, mono", GallerySample.Input, "Input.Mono", "KX7PM-4R2WQ", false, Field),
        new("Combo box", GallerySample.ComboBox, null, "Medium", false, Hoverable),
        new("Check box", GallerySample.CheckBox, null, "KB article", false, Pressable),
        new("Check box, on", GallerySample.CheckBox, null, "Ticket note", true, Pressable),
        new("Radio", GallerySample.RadioButton, null, "Discussion", false, Pressable),
        new("Radio, on", GallerySample.RadioButton, null, "Internal", true, Pressable),
        new("Segmented", GallerySample.Segment, "Segment", "Discussion", false, Pressable),
        new("Check chip", GallerySample.CheckChip, "CheckChip", "anydesk", false, Pressable),
        new("Check chip, on", GallerySample.CheckChip, "CheckChip", "screenconnect", true, Pressable),
        new("Slider", GallerySample.Slider, null, "14", false, Hoverable),
    ];

    /// <summary>A badge in every <c>theme:Badge.Tone</c> the Badge template draws, with its words and glyph key.</summary>
    public static IReadOnlyList<(string Tone, string Words, string? Glyph)> Badges { get; } =
    [
        ("Neutral", "Draft", null),
        ("Accent", "Suggested", null),
        ("Success", "Published", "Icon.Check"),
        ("Warning", "Low confidence", "Icon.Warning"),
        ("Danger", "Failed", null),
        ("Scope", "Not capturing", null),
    ];

    /// <summary>A banner in every <c>theme:Banner.Tone</c> the Banner template draws.</summary>
    public static IReadOnlyList<(string Tone, string? Title, string Words)> Banners { get; } =
    [
        ("Info", null, "Drafted on this PC only. Quality may be lower."),
        ("Success", null, "Published to ticket #48213."),
        ("Warning", "Some steps may be missing", "Capture started late or was interrupted."),
        ("Danger", null, "ConnectWise rejected the API key. Ask your ScreenTrail admin to update the ConnectWise connection in the web admin."),
    ];
}
