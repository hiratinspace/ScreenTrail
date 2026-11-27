using System.Globalization;
using System.Xml.Linq;

namespace ScreenTrail.Tests.Accessibility;

/// <summary>
/// A field's label sits 4 above it on every Settings page (design system 10.2, review finding B-14): the gap
/// was 4 on some fields and 8 on others, so labels drifted from the fields they name.
/// </summary>
public sealed class LabelGapTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    [Theory]
    [InlineData("Activation", "ActivationPane.xaml")]
    [InlineData("Capture", "CapturePane.xaml")]
    [InlineData("Privacy", "PrivacyPane.xaml")]
    [InlineData("Integrations", "IntegrationsPane.xaml")]
    public void ALabelIsFourAboveTheFieldItNames(string folder, string file)
    {
        var pane = Load(folder, file);
        var gaps = new List<string>();
        var measured = 0;

        // A field label is a Field.Label with an access key since review finding E-20.
        foreach (var label in pane.Descendants().Where(t => (string?)t.Attribute("Style") is "{StaticResource Text.Label}" or "{StaticResource Field.Label}"))
        {
            // A label in a heading row (with its Changed badge) is measured from the row.
            var above = label.Parent?.Name == Wpf + "StackPanel" && (string?)label.Parent.Attribute("Orientation") == "Horizontal" ? label.Parent : label;
            var next = above.ElementsAfterSelf().FirstOrDefault();
            if (next is null || !IsField(next))
            {
                continue;
            }

            measured++;
            var gap = Side((string?)above.Attribute("Margin"), 3) + Side((string?)next.Attribute("Margin"), 1);
            if (gap != 4)
            {
                gaps.Add($"{label.Attribute("Text") ?? label.Attribute("Content")}: {gap}");
            }
        }

        Assert.True(measured > 0, $"no label above a field was found in {file}");
        Assert.Empty(gaps);
    }

    private static bool IsField(XElement element) =>
        element.Name.LocalName is "TextBox" or "ComboBox" or "PasswordBox"
        || (element.Name.LocalName is "StackPanel" or "Grid" && element.Elements().Any(e => e.Name.LocalName is "TextBox" or "ComboBox" or "PasswordBox"));

    /// <summary>One side of a Thickness: 0 left, 1 top, 2 right, 3 bottom.</summary>
    private static double Side(string? margin, int side)
    {
        if (margin is null)
        {
            return 0;
        }

        var parts = margin.Split(',').Select(p => double.Parse(p, CultureInfo.InvariantCulture)).ToArray();
        return parts.Length switch
        {
            1 => parts[0],
            2 => side % 2 == 0 ? parts[0] : parts[1],
            _ => parts[side],
        };
    }

    private static XElement Load(string folder, string file)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "client", "ScreenTrail.UI")))
        {
            directory = directory.Parent;
        }

        return XDocument.Load(Path.Combine(directory!.FullName, "client", "ScreenTrail.UI", "Settings", folder, file)).Root!;
    }
}
