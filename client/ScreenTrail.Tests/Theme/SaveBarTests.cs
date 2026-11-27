using System.Xml.Linq;

namespace ScreenTrail.Tests.Theme;

/// <summary>The Save bar's spacing and its Saving look (review finding B-46).</summary>
public sealed class SaveBarTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void ALoadingPrimaryKeepsItsFillAfterTheDisabledLook()
    {
        var primary = Keyed("Button.Primary");
        var triggers = primary.Element(Wpf + "Style.Triggers")!.Elements(Wpf + "Trigger").ToList();
        var disabled = triggers.FindIndex(t => (string?)t.Attribute("Property") == "IsEnabled");
        var busy = triggers.FindIndex(t => (string?)t.Attribute("Property") == "theme:Busy.Active");

        Assert.True(busy > disabled, "the loading look must come after disabled to win over it");
        Assert.Contains(triggers[busy].Elements(Wpf + "Setter"), s => (string?)s.Attribute("Value") == "{DynamicResource Brush.accent.primary}");
    }

    [Fact]
    public void TheBarIsFortyEightHighAndItsFailureLineSitsUnderItsWords()
    {
        var bar = Keyed("SaveBar").Elements(Wpf + "Setter").ToDictionary(s => (string)s.Attribute("Property")!, s => (string?)s.Attribute("Value"));
        Assert.Equal("16,8", bar["Padding"]);
        Assert.Equal("{DynamicResource Layout.save-bar-height}", bar["MinHeight"]);

        var settings = Pane();
        var failure = settings.Descendants(Wpf + "TextBlock").Single(t => (string?)t.Attribute("Text") == "{Binding FailureMessage}");
        Assert.Equal("24,4,0,0", (string?)failure.Attribute("Margin"));

        var save = settings.Descendants(Wpf + "Button").Single(b => (string?)b.Attribute("Command") == "{Binding SaveCommand}");
        Assert.Equal("{Binding IsSaving}", (string?)save.Attribute(XNamespace.Get("clr-namespace:ScreenTrail.UI.Theme") + "Busy.Active"));
        Assert.Contains(save.Descendants(Wpf + "Control"), c => (string?)c.Attribute("Style") == "{StaticResource ProgressRing}");
        Assert.Contains(save.Descendants(Wpf + "TextBlock"), t => (string?)t.Attribute("Text") == "Saving");
    }

    private static XElement Keyed(string key) => ChoiceStatesTests.Components().Elements(Wpf + "Style").Single(s => (string?)s.Attribute(X + "Key") == key);

    private static XElement Pane()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(directory!.FullName, "client", "ScreenTrail.UI")))
        {
            directory = directory.Parent;
        }

        return XDocument.Load(Path.Combine(directory.FullName, "client", "ScreenTrail.UI", "Settings", "SettingsPane.xaml")).Root!;
    }
}
