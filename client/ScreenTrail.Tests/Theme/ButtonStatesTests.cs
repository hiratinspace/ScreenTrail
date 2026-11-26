using System.Xml.Linq;

namespace ScreenTrail.Tests.Theme;

/// <summary>
/// The button states (design system 10.1; review findings A-13, A-32, B-02, B-03), read off Components.xaml.
/// Pressed darkens the fill and never touches the label or the opacity; disabled is its own colours; the
/// compact size is one property on the shared base.
/// </summary>
public sealed class ButtonStatesTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Theory]
    [InlineData("Button.Secondary", "Brush.bg.pressed")]
    [InlineData("Button.Ghost", "Brush.bg.pressed")]
    [InlineData("Button.Primary", "Brush.accent.primary.pressed")]
    public void PressedDarkensTheFillAndLeavesTheLabel(string key, string fill)
    {
        var pressed = Trigger(Style(key), "IsPressed");

        Assert.Equal($"{{DynamicResource {fill}}}", Setter(pressed, "Background"));
        Assert.Null(Setter(pressed, "Opacity"));
        Assert.Null(Setter(pressed, "Foreground"));
    }

    [Fact]
    public void NoButtonStateIsAnOpacity()
    {
        foreach (var key in new[] { "Button.Base", "Button.Secondary", "Button.Primary", "Button.Ghost", "Button.Danger" })
        {
            Assert.DoesNotContain(Style(key).Descendants(Wpf + "Setter"), s => (string?)s.Attribute("Property") == "Opacity");
        }
    }

    [Fact]
    public void DisabledIsItsOwnFillAndLabelOnTheSharedBase()
    {
        var disabled = Trigger(Style("Button.Base"), "IsEnabled");

        Assert.Equal("{DynamicResource Brush.bg.disabled}", Setter(disabled, "Background"));
        Assert.Equal("{DynamicResource Brush.text.disabled}", Setter(disabled, "Foreground"));
        Assert.Equal("Arrow", Setter(disabled, "Cursor"));

        // Primary repeats it after its own hover and pressed, so it wins over both.
        Assert.Equal("{DynamicResource Brush.text.disabled}", Setter(Trigger(Style("Button.Primary"), "IsEnabled"), "Foreground"));
    }

    [Fact]
    public void GhostHoverFillsWithoutGrowingAnEdge()
    {
        var hover = Trigger(Style("Button.Ghost"), "IsMouseOver");

        Assert.Equal("{DynamicResource Brush.bg.hover}", Setter(hover, "Background"));
        Assert.Null(Setter(hover, "BorderBrush"));
    }

    [Fact]
    public void EveryButtonStyleIsBuiltOnTheBaseWithTheGridGeometry()
    {
        foreach (var key in new[] { "Button.Secondary", "Button.Primary", "Button.Ghost", "Button.Danger" })
        {
            Assert.Equal("{StaticResource Button.Base}", (string?)Style(key).Attribute("BasedOn"));
        }

        var base_ = Style("Button.Base");
        Assert.Equal("{DynamicResource Padding.control}", Setter(base_, "Padding"));
        Assert.Equal("{DynamicResource Layout.button-min-width}", Setter(base_, "MinWidth"));

        var compact = Trigger(base_, "theme:Density.Compact");
        Assert.Equal("{DynamicResource Layout.control-height-compact}", Setter(compact, "MinHeight"));
        Assert.Equal("{DynamicResource Padding.control-compact}", Setter(compact, "Padding"));
        Assert.Equal("{DynamicResource Layout.button-min-width-compact}", Setter(compact, "MinWidth"));
    }

    [Fact]
    public void ADestructiveDialogConfirmsWithTheSolidDangerButton()
    {
        // Review finding B-05: the outline was weaker than the safe button beside it.
        var solid = Style("Button.DangerSolid");
        Assert.Equal("{DynamicResource Brush.danger.fill}", Setter(solid, "Background"));
        Assert.Equal("{DynamicResource Brush.text.on-accent}", Setter(solid, "Foreground"));
        Assert.Equal("{DynamicResource Brush.danger.fill.hover}", Setter(Trigger(solid, "IsPressed"), "Background"));

        var code = File.ReadAllText(Path.Combine(Ui(), "Shell", "ConfirmWindow.xaml.cs"));
        Assert.Contains("ConfirmTone.Danger ? \"Button.DangerSolid\"", code, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Button.Danger")]
    [InlineData("Button.DangerSolid")]
    public void ADestructiveButtonLeadsWithTheDeleteIcon(string key)
    {
        // Review finding B-06: in high contrast, red is gone and Delete everything looked like Export.
        Assert.Equal("{StaticResource Icon.Delete}", Setter(Style(key), "theme:ButtonIcon.Leading"));
    }

    [Fact]
    public void TheButtonTemplateDrawsTheLeadingIconOnlyWhenOneIsSet()
    {
        var template = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Theme", "Components.xaml")).Root!
            .Elements(Wpf + "ControlTemplate").Single(t => (string?)t.Attribute(X + "Key") == "Button.Template");
        XNamespace controls = "clr-namespace:ScreenTrail.UI.Controls";

        var leading = Assert.Single(template.Descendants(controls + "Glyph"));
        Assert.Equal("{TemplateBinding theme:ButtonIcon.Leading}", (string?)leading.Attribute("Text"));
        Assert.Equal("{TemplateBinding Foreground}", (string?)leading.Attribute("Foreground"));
        var unset = Assert.Single(template.Descendants(Wpf + "Trigger"), t => (string?)t.Attribute("Property") == "theme:ButtonIcon.Leading");
        Assert.Equal("{x:Null}", (string?)unset.Attribute("Value"));
    }

    [Fact]
    public void SettingsHasOneFilledButtonAndItIsSave()
    {
        // Review finding B-04 (design system 1.2 rule 1, one Primary per window): Install update was filled
        // as well as the Save bar's Save. Activate stays Primary: it is the only action on its page.
        var updates = XDocument.Load(Path.Combine(Ui(), "Update", "UpdatesPane.xaml"));

        var install = Assert.Single(updates.Descendants(Wpf + "Button"), b => (string?)b.Attribute("Content") == "Install update");
        Assert.Equal("{StaticResource Button.Secondary}", (string?)install.Attribute("Style"));
    }

    [Theory]
    [InlineData("Review/NoteEditorView.xaml", "{Binding DataContext.ConfirmStepCommand, RelativeSource={RelativeSource AncestorType=UserControl}}")]
    [InlineData("Review/NoteEditorView.xaml", "{Binding UndoDeleteCommand}")]
    [InlineData("Settings/Integrations/IntegrationsPane.xaml", "{Binding DataContext.UnmapCommand, RelativeSource={RelativeSource AncestorType=ItemsControl}}")]
    public void AButtonInsideAStepAToastOrARowIsCompact(string file, string command)
    {
        // Review finding B-03: Looks right, Undo and Forget sat at full height inside a step, a toast and a row.
        var view = XDocument.Load(Path.Combine(Ui(), file));
        XNamespace theme = "clr-namespace:ScreenTrail.UI.Theme";

        var button = Assert.Single(view.Descendants(Wpf + "Button"), b => (string?)b.Attribute("Command") == command);
        Assert.Equal("True", (string?)button.Attribute(theme + "Density.Compact"));
    }

    private static string Ui()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "client", "ScreenTrail.UI")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory?.FullName ?? throw new DirectoryNotFoundException("client"), "client", "ScreenTrail.UI");
    }

    private static XElement Style(string key) =>
        XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Theme", "Components.xaml")).Root!
            .Elements(Wpf + "Style").Single(s => (string?)s.Attribute(X + "Key") == key);

    private static XElement Trigger(XElement style, string property) =>
        Assert.Single(style.Descendants(Wpf + "Trigger"), t => (string?)t.Attribute("Property") == property);

    private static string? Setter(XElement scope, string property) =>
        (string?)scope.Elements(Wpf + "Setter").FirstOrDefault(s => (string?)s.Attribute("Property") == property)?.Attribute("Value");
}
