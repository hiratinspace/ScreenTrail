using System.Xml.Linq;
using ScreenTrail.Tests.Theme;

namespace ScreenTrail.Tests.Shell;

/// <summary>
/// Review finding D-26: the shell's panes are hosted once each and switched by visibility, not thrown away
/// by one ContentControl on every switch; the incoming one fades in, and a Review pane hidden by a switch
/// writes what was typed (T-169). The window's code is read as text: the Mac cannot run a WPF window.
/// </summary>
public sealed class ShellPaneHostTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void ThePanesAreHostedOnceAndSwitchedByVisibility()
    {
        var pane = EmptyStateTests.View("Shell", "ShellWindow.xaml").Descendants().Single(e => (string?)e.Attribute(Xaml + "Name") == "Pane");
        Assert.Equal(Wpf + "Grid", pane.Name);
        Assert.Null(pane.Attribute("Content"));

        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(folder.FullName, "ScreenTrail.UI")))
        {
            folder = folder.Parent!;
        }

        var code = File.ReadAllText(Path.Combine(folder.FullName, "ScreenTrail.UI", "Shell", "ShellWindow.xaml.cs"));
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Count(code, @"HostPanes\(\);"));
        Assert.Contains("ReferenceEquals(other, _model.HeldReview)", code, StringComparison.Ordinal);
        Assert.Contains("leaving.Note.FlushAsync()", code, StringComparison.Ordinal);
        Assert.Contains("Theme.ThemeManager.ReduceMotion", code, StringComparison.Ordinal);
    }
}
