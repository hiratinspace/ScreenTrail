using System.Drawing;
using System.Runtime.Versioning;
using ScreenTrail.UI.Theme;
using Forms = System.Windows.Forms;
using Media = System.Windows.Media;

namespace ScreenTrail.UI.Tray;

/// <summary>
/// The tray menu drawn in the app's tokens (Spec §2.1, §3; S1 as built v0.6.10).
///
/// Windows Forms' default renderer drew the menu in its own palette, with a gradient down the image
/// margin, which §2.0's flat-fill rule and §2.1's tokens both rule out, and its greyed disabled text fell
/// under 4.5:1. This reads every colour from the theme dictionary the windows use, at paint time, so the
/// menu follows a theme change and, in high contrast, the system colours the dictionary maps to: there
/// the row under the pointer or the keyboard is the Highlight fill with HighlightText, as in every other
/// Windows menu, because the tints are all the window colour.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class TrayMenuRenderer : Forms.ToolStripRenderer
{
    /// <summary>The colour a token has in the theme on show, or the system colour it stands for when no theme is loaded.</summary>
    internal static Color Token(string name)
    {
        if (System.Windows.Application.Current?.TryFindResource($"Token.{name}") is Media.Color colour)
        {
            return Color.FromArgb(colour.A, colour.R, colour.G, colour.B);
        }

        // A tray built with no WPF application (the Windows unit tests): the system colours the
        // high-contrast dictionary maps each token to, which are always legible together.
        return name switch
        {
            "bg.surface" or "bg.hover" or "bg.pressed" or "bg.selected" or "bg.danger" => SystemColors.Window,
            "text.primary" or "text.secondary" or "state.danger" => SystemColors.WindowText,
            "text.muted" => SystemColors.GrayText,
            "accent.primary" => SystemColors.Highlight,
            "text.on-accent" => SystemColors.HighlightText,
            "border.subtle" => SystemColors.GrayText,
            _ => SystemColors.WindowFrame,
        };
    }

    /// <summary>Whether the theme on show is high contrast, where the tints are not seen.</summary>
    internal static bool HighContrast => ThemeManager.Current == AppTheme.HighContrast || System.Windows.Application.Current is null;

    /// <summary>Whether the menu's ground is light, which picks the light-taskbar glyph for the state line.</summary>
    internal static bool LightGround => Token("bg.surface").GetBrightness() > 0.5f;

    /// <summary>What the text of a row is drawn in: the selection's text, the state line, a hint, or a disabled item.</summary>
    internal static Color TextColour(Forms.ToolStripItem item, bool hotkey)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Enabled && item.Selected && HighContrast)
        {
            return Token("text.on-accent");
        }

        return (item.Tag as TrayRowTag)?.Kind switch
        {
            Core.Shell.TrayRowKind.State => Token("text.primary"),
            Core.Shell.TrayRowKind.Hint => Token("text.secondary"),

            // A disabled row keeps its words at 4.5:1 (Spec §2.1, v0.5.0) rather than the system's grey.
            _ when !item.Enabled => Token("text.muted"),

            // The chord is quieter than the words (review finding C-T4), and Discard reads as what it is.
            _ when hotkey => Token("text.muted"),
            _ when Destructive(item) => Token("state.danger"),
            _ => Token("text.primary"),
        };
    }

    protected override void OnRenderToolStripBackground(Forms.ToolStripRenderEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        using var fill = new SolidBrush(Token("bg.surface"));
        e.Graphics.FillRectangle(fill, e.AffectedBounds);
    }

    protected override void OnRenderImageMargin(Forms.ToolStripRenderEventArgs e)
    {
        // Flat: the margin is the menu's own fill (§2.0, no gradients).
    }

    /// <summary>
    /// Whether Windows rounds the menu and draws its edge and shadow (Windows 11, build 22000 and up), as
    /// TrayIconHost asks it to (review finding C-T2). Windows 10 ignores the request: the menu stays square
    /// with its system drop shadow and the 1 px edge drawn here.
    /// </summary>
    internal static bool SystemRoundsTheMenu => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);

    protected override void OnRenderToolStripBorder(Forms.ToolStripRenderEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (SystemRoundsTheMenu)
        {
            return;
        }

        using var edge = new Pen(Token("border.strong"));
        var bounds = new Rectangle(Point.Empty, e.ToolStrip.Size);
        e.Graphics.DrawRectangle(edge, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
    }

    protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var item = e.Item;
        if (!item.Enabled || !(item.Selected || item.Pressed))
        {
            return;
        }

        // The row under the pointer or the arrow keys (design system 10.7, review finding C-T1): a filled
        // rounded rectangle, radius 4, the full width inside the menu's padding, with no border; pressed
        // is a step darker. Iris does not appear: a lavender tint inside a 2 px Iris box was the 2009 menu
        // look. High contrast: the Highlight fill, as in every other Windows menu.
        var scale = Scale(e.ToolStrip);
        using var fill = new SolidBrush(BackgroundColour(item));
        using var shape = Rounded(new RectangleF(0, 0, item.Width, item.Height), 4 * scale);
        var smoothing = e.Graphics.SmoothingMode;
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        e.Graphics.FillPath(fill, shape);
        e.Graphics.SmoothingMode = smoothing;
    }

    /// <summary>The fill under a row the pointer or the keyboard is on: hover, pressed, or Highlight in high contrast.</summary>
    internal static Color BackgroundColour(Forms.ToolStripItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return HighContrast ? Token("accent.primary")
            : Destructive(item) ? Token("bg.danger")
            : Token(item.Pressed ? "bg.pressed" : "bg.hover");
    }

    /// <summary>Whether the row deletes something: "Discard session…", drawn in the danger colour (review finding C-T4).</summary>
    internal static bool Destructive(Forms.ToolStripItem item) => (item.Tag as TrayRowTag)?.Command == Core.Shell.TrayCommand.Discard;

    /// <summary>The chord's face: 12 px, as TrayIconHost sets it from the tokens; the row's own when unset.</summary>
    internal Font? ShortcutFont { get; set; }

    /// <summary>The menu's scale: its DPI over 96, since every pixel value in design system 10.7 is at 96.</summary>
    internal static float Scale(Forms.ToolStrip? strip) => (strip?.DeviceDpi ?? 96) / 96f;

    private static System.Drawing.Drawing2D.GraphicsPath Rounded(RectangleF bounds, float radius)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        var d = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>
    /// A row's words at 12 from its left edge and its chord in 12 px, right aligned 12 from its right edge
    /// (design system 10.7, review finding C-T4), with GDI's TextRenderer for ClearType.
    /// </summary>
    protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var item = e.Item;
        var hotkey = item is Forms.ToolStripMenuItem { ShortcutKeyDisplayString: { Length: > 0 } shown } && e.Text == shown;
        var colour = TextColour(item, hotkey);
        var scale = Scale(e.ToolStrip);
        var inset = (int)Math.Round(12 * scale);
        if (item.Tag is TrayRowTag { Kind: Core.Shell.TrayRowKind.State } state)
        {
            Header(e.Graphics, item, e.Text ?? string.Empty, e.TextFont ?? item.Font, colour, scale, state.Detail, ShortcutFont ?? item.Font);
            return;
        }

        const Forms.TextFormatFlags Line = Forms.TextFormatFlags.VerticalCenter | Forms.TextFormatFlags.SingleLine;
        if (hotkey)
        {
            var chord = new Rectangle(0, 0, item.Width - inset, item.Height);
            Forms.TextRenderer.DrawText(e.Graphics, e.Text, ShortcutFont ?? e.TextFont, chord, colour, Line | Forms.TextFormatFlags.Right | Forms.TextFormatFlags.NoPrefix);
            return;
        }

        // The words run up to where the layout put their own end, so they never reach the chord.
        var words = new Rectangle(inset, 0, Math.Max(0, Math.Min(item.Width - inset, e.TextRectangle.Right) - inset), item.Height);
        var prefix = e.TextFormat & (Forms.TextFormatFlags.HidePrefix | Forms.TextFormatFlags.NoPrefix);
        Forms.TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, words, colour, Line | Forms.TextFormatFlags.Left | Forms.TextFormatFlags.EndEllipsis | prefix);
    }

    /// <summary>The height of the state row's first line, at 96 dpi (design system 10.7).</summary>
    internal const int HeaderHeight = 36;

    /// <summary>
    /// The state row (design system 10.7, review finding C-T3): the tray's own glyph for the state, 16 px
    /// at x 12 and drawn as it is, in its colours, then the words at x 36 in the semibold face. The row
    /// is disabled so it cannot be chosen, and WinForms drew its image through the disabled filter, so the
    /// red dot and the amber bars were grey in the one place the menu restates the state (INV-4).
    /// </summary>
    private static void Header(Graphics graphics, Forms.ToolStripItem item, string text, Font font, Color colour, float scale, string? detail, Font small)
    {
        var layout = HeaderLayout(item.Width, font, detail, small, scale);
        if (item.Image is { } glyph)
        {
            var side = (int)Math.Round(16 * scale);
            graphics.DrawImage(glyph, new Rectangle((int)Math.Round(12 * scale), layout.State.Top + ((layout.State.Height - side) / 2), side, side));
        }

        Forms.TextRenderer.DrawText(graphics, text, font, layout.State, colour, Forms.TextFormatFlags.VerticalCenter | Forms.TextFormatFlags.SingleLine | Forms.TextFormatFlags.Left | Forms.TextFormatFlags.EndEllipsis | Forms.TextFormatFlags.NoPrefix);
        if (detail is not null && layout.Counts is { } counts)
        {
            // What waits, on its own line in Caption text.muted, 4 below the state (review finding C-T5).
            Forms.TextRenderer.DrawText(graphics, detail, small, counts, Token("text.muted"), CountsFormat);
        }
    }

    private const Forms.TextFormatFlags CountsFormat = Forms.TextFormatFlags.Left | Forms.TextFormatFlags.Top | Forms.TextFormatFlags.WordBreak | Forms.TextFormatFlags.NoPrefix;

    /// <summary>The widest the menu grows (design system 10.7): the header's second line wraps rather than widening it.</summary>
    internal const int MaxWidth = 360;

    /// <summary>
    /// Where the state row's parts go, for drawing it and for sizing it alike: the state's line, the
    /// counts' block under it when there are counts, and the row's height. With no counts the state is
    /// centred in 36; with counts it sits 6 from the top, the counts 4 below it, and 8 under them.
    /// </summary>
    internal static (Rectangle State, Rectangle? Counts, int Height) HeaderLayout(int width, Font font, string? detail, Font small, float scale)
    {
        var left = (int)Math.Round(36 * scale);
        var room = Math.Max(1, width - left - (int)Math.Round(12 * scale));
        if (detail is null)
        {
            var line = (int)Math.Round(HeaderHeight * scale);
            return (new Rectangle(left, 0, room, line), null, line);
        }

        var top = (int)Math.Round(6 * scale);
        var stateHeight = Forms.TextRenderer.MeasureText("Ag", font, new Size(room, int.MaxValue), Forms.TextFormatFlags.SingleLine).Height;
        var countsTop = top + stateHeight + (int)Math.Round(4 * scale);
        var countsHeight = Forms.TextRenderer.MeasureText(detail, small, new Size(room, int.MaxValue), CountsFormat).Height;
        var height = Math.Max((int)Math.Round(HeaderHeight * scale), countsTop + countsHeight + (int)Math.Round(8 * scale));
        return (new Rectangle(left, top, room, stateHeight), new Rectangle(left, countsTop, room, countsHeight), height);
    }

    /// <summary>Nothing: the one image, the state row's glyph, is drawn undimmed by <see cref="Header"/>.</summary>
    protected override void OnRenderItemImage(Forms.ToolStripItemImageRenderEventArgs e)
    {
    }

    protected override void OnRenderSeparator(Forms.ToolStripSeparatorRenderEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        using var line = new Pen(Token("border.subtle"));
        var middle = e.Item.Height / 2;
        e.Graphics.DrawLine(line, 8, middle, e.Item.Width - 8, middle);
    }
}

/// <summary>What the host hangs on each menu item, so the renderer knows a state line from a hint from an item.</summary>
internal sealed record TrayRowTag(Core.Shell.TrayRowKind Kind, Core.Shell.TrayCommand Command, string? Detail = null);
