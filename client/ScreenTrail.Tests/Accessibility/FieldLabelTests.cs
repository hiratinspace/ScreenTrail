using System.Xml.Linq;

namespace ScreenTrail.Tests.Accessibility;

/// <summary>
/// Every field in Settings' Privacy and Integrations pages has its own visible label, and its accessible name is
/// those words (review findings B-13, E-26; WCAG 2.5.3 label in name). Two buttons in one pane both named "Add"
/// are told apart.
/// </summary>
public sealed class FieldLabelTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private static readonly string[] FieldWidths = ["{DynamicResource Layout.field-narrow}", "{DynamicResource Layout.field}", "{DynamicResource Layout.field-wide}"];

    [Theory]
    [InlineData("Privacy", "PrivacyPane.xaml")]
    [InlineData("Integrations", "IntegrationsPane.xaml")]
    public void EachFieldIsNamedByAVisibleLabel(string folder, string file)
    {
        var pane = Load(folder, file);
        // A field label is a Label with an access key since review finding E-20; its words are its Content
        // without the underscore.
        var labels = pane.Descendants(Wpf + "TextBlock").Where(t => (string?)t.Attribute("Style") == "{StaticResource Text.Label}")
            .Select(t => (string?)t.Attribute("Text"))
            .Concat(pane.Descendants(Wpf + "Label").Select(l => ((string?)l.Attribute("Content"))?.Replace("_", string.Empty, StringComparison.Ordinal)))
            .ToHashSet();

        var fields = pane.Descendants().Where(e => e.Name.LocalName is "TextBox" or "ComboBox")
            .Where(f => !((string?)f.Attribute("AutomationProperties.Name") ?? "{").StartsWith('{'))
            .ToList();

        Assert.NotEmpty(fields);
        Assert.All(fields, f => Assert.Contains((string?)f.Attribute("AutomationProperties.Name"), labels));
    }

    [Theory]
    [InlineData("Capture", "CapturePane.xaml")]
    [InlineData("Privacy", "PrivacyPane.xaml")]
    [InlineData("Activation", "ActivationPane.xaml")]
    public void AFieldsWidthIsOneOfTheThreeFieldWidths(string folder, string file)
    {
        // Review finding B-15: 160, 240, 320 and 360 were four unrelated literals, so field ends did not line up.
        var widths = Load(folder, file).Descendants().Where(e => e.Name.LocalName is "TextBox" or "ComboBox")
            .Select(f => (string?)f.Attribute("Width")).Where(w => w is not null).ToList();

        Assert.All(widths, w => Assert.Contains(w, FieldWidths));
    }

    [Fact]
    public void PrivacysTwoAddButtonsSayWhatTheyAdd()
    {
        var names = Load("Privacy", "PrivacyPane.xaml").Descendants(Wpf + "Button")
            .Where(b => (string?)b.Attribute("Content") == "Add")
            .Select(b => (string?)b.Attribute("AutomationProperties.Name"))
            .ToList();

        Assert.Equal(["Add app", "Add pattern"], names);
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
