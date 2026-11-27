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
            "text.primary" or "text.secondary" => SystemColors.WindowText,
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
            _ when hotkey => Token("text.secondary"),
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
        return HighContrast ? Token("accent.primary") : Token(item.Pressed ? "bg.pressed" : "bg.hover");
    }

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

    protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var hotkey = e.Item is Forms.ToolStripMenuItem { ShortcutKeyDisplayString: { Length: > 0 } shown } && e.Text == shown;
        Forms.TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, e.TextRectangle, TextColour(e.Item, hotkey), e.TextFormat);
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
internal sealed record TrayRowTag(Core.Shell.TrayRowKind Kind, Core.Shell.TrayCommand Command);
