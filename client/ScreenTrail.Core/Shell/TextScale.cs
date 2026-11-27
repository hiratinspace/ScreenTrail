namespace ScreenTrail.Core.Shell;

/// <summary>
/// Windows' "Text size" (Settings, Accessibility), followed (review finding D-14). Windows keeps it as
/// <c>HKCU\Software\Microsoft\Accessibility\TextScaleFactor</c>, a whole percentage from 100 to 225, and
/// missing means 100. ScreenTrail's type sizes, line heights and the heights of the rows and the pill that
/// hold text are the tokens multiplied by it and rounded to whole pixels, so a technician at 125 or 150%
/// reads ScreenTrail at the size every Microsoft app around it uses.
/// </summary>
public static class TextScale
{
    public const int Smallest = 100;

    public const int Largest = 225;

    /// <summary>The tokens that scale: every type size, every line height, and the fixed heights that hold a line of text.</summary>
    public static IReadOnlyList<string> Keys { get; } =
    [
        "Size.xs", "Size.sm", "Size.md", "Size.lg", "Size.xl", "Size.2xl", "Size.mono",
        "Leading.micro", "Leading.caption", "Leading.body", "Leading.section", "Leading.title", "Leading.display",
        "Layout.row-height", "Layout.group-row-height", "Layout.nav-row-height", "Layout.receipt-row-height", "Layout.flow-row-height",
        "Layout.hud-height",
    ];

    /// <summary>The factor for what Windows has stored: 1 for nothing, or a value it would not write.</summary>
    public static double Factor(object? stored) => stored is int percent and >= Smallest and <= Largest ? percent / 100d : 1d;

    /// <summary>A token at <paramref name="factor"/>, on whole pixels.</summary>
    public static double Scaled(double token, double factor) => Math.Round(token * factor, MidpointRounding.AwayFromZero);
}
