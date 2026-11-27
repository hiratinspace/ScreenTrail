using System.Xml.Linq;

namespace ScreenTrail.Tests.Theme;

/// <summary>
/// Spec §7: focus is seen at 3:1. A scroll area that takes keyboard focus draws the app's focus ring, not
/// WPF's default dotted rectangle, which the Windows audit measured on the shortcut sheet at 1 changed
/// pixel of the 712 it needs (CI on ST-127).
/// </summary>
public sealed class FocusableScrollAreaTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    [Fact]
    public void EveryScrollAreaThatTakesFocusDrawsTheFocusRing()
    {
        var ui = Ui();
        var focusable = Directory.EnumerateFiles(ui, "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(f => XDocument.Load(f).Descendants(Wpf + "ScrollViewer")
                .Where(s => (string?)s.Attribute("IsTabStop") == "True")
                .Select(s => (File: Path.GetRelativePath(ui, f), Ring: (string?)s.Attribute("FocusVisualStyle"))))
            .ToList();

        // The control: the shortcut sheet's scroll area takes focus, so an empty set cannot pass.
        Assert.Contains(focusable, s => s.File.EndsWith("ShortcutSheet.xaml", StringComparison.Ordinal));
        Assert.All(focusable, s => Assert.StartsWith("{StaticResource FocusRing", s.Ring ?? string.Empty, StringComparison.Ordinal));
    }

    private static string Ui()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "client", "ScreenTrail.UI");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("client/ScreenTrail.UI was not found above the test output directory");
    }
}
