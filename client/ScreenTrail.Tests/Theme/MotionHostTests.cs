using System.Xml.Linq;

namespace ScreenTrail.Tests.Theme;

/// <summary>
/// Review finding D-32: the motion helper's hosts. The toasts rise in, the service banner and the Save bar
/// fade in, and reduce motion follows DS 9.5 (animations off, a remote session, software rendering). The
/// helper is read as text: the Mac cannot run WPF.
/// </summary>
public sealed class MotionHostTests
{
    private static readonly XNamespace Theme = "clr-namespace:ScreenTrail.UI.Theme";

    [Theory]
    [InlineData("Review", "FilmstripView.xaml", "Enter", "8")]
    [InlineData("Review", "NoteEditorView.xaml", "Enter", "8")]
    [InlineData("Settings", "SettingsPane.xaml", "FadeIn", "True")]
    [InlineData("Shell", "ShellWindow.xaml", "FadeIn", "True")]
    public void TheHostMoves(string folder, string file, string motion, string value)
    {
        var view = EmptyStateTests.View(folder, file);
        Assert.Contains(view.Descendants(), e => (string?)e.Attribute(Theme + ("Motion." + motion)) == value);
    }

    [Fact]
    public void ReduceMotionIsTheDesignSystemsRule()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(folder.FullName, "ScreenTrail.UI")))
        {
            folder = folder.Parent!;
        }

        var manager = File.ReadAllText(Path.Combine(folder.FullName, "ScreenTrail.UI", "Theme", "ThemeManager.cs"));
        Assert.Contains("ReduceMotion => !ClientAreaAnimation() || RemoteSession() || SoftwareRendering()", manager, StringComparison.Ordinal);
        Assert.Contains("SystemParameters.IsRemoteSession", manager, StringComparison.Ordinal);
        Assert.Contains("RenderCapability.Tier >> 16", manager, StringComparison.Ordinal);

        var motion = File.ReadAllText(Path.Combine(folder.FullName, "ScreenTrail.UI", "Theme", "Motion.cs"));
        Assert.Contains("!ThemeManager.ReduceMotion && !Waiting.AtOnce", motion, StringComparison.Ordinal);
        Assert.DoesNotContain("Storyboard", motion, StringComparison.Ordinal);
    }
}
