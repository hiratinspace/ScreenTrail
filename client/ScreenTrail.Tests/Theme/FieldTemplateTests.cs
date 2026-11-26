using System.Xml.Linq;

namespace ScreenTrail.Tests.Theme;

/// <summary>
/// A field that is wrong looks wrong, with its message 4 below it, and an empty text box can show an example
/// (design system 10.2; review findings B-12, B-13), drawn by the field's own template from theme:Field.
/// </summary>
public sealed class FieldTemplateTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void ATextFieldDrawsItsErrorEdgeAndLineFromTheFieldProperties()
    {
        var template = Components().Elements(Wpf + "ControlTemplate").Single(t => (string?)t.Attribute(X + "Key") == "Input.Template");

        AssertError(template, Components().Elements(Wpf + "Style").Single(s => (string?)s.Attribute(X + "Key") == "Input.Default"));
        var placeholder = Named(template, "Placeholder");
        Assert.Equal("{TemplateBinding theme:Field.Placeholder}", (string?)placeholder.Attribute("Text"));
        Assert.Equal("{DynamicResource Brush.text.muted}", (string?)placeholder.Attribute("Foreground"));
        Assert.Equal("False", (string?)placeholder.Attribute("IsHitTestVisible"));
        Assert.Contains(template.Descendants(Wpf + "Trigger"), t => (string?)t.Attribute("Property") == "Text" && (string?)t.Attribute("Value") == "");
    }

    [Fact]
    public void AComboDrawsTheSameError()
    {
        var combo = Components().Elements(Wpf + "Style").Single(s => s.Attribute(X + "Key") is null && (string?)s.Attribute("TargetType") == "ComboBox");

        AssertError(combo, combo);
    }

    [Fact]
    public void TheOldErrorStyleIsGone()
    {
        // Input.Error was a red edge with no message, used only in the Gallery.
        Assert.DoesNotContain(Components().Elements(Wpf + "Style"), s => (string?)s.Attribute(X + "Key") == "Input.Error");
    }

    private static void AssertError(XElement scope, XElement style)
    {
        var edge = Named(scope, "ErrorEdge");
        Assert.Equal("{DynamicResource Brush.state.danger}", (string?)edge.Attribute("BorderBrush"));
        Assert.Equal("False", (string?)edge.Attribute("IsHitTestVisible"));

        var line = Named(scope, "ErrorLine");
        Assert.Equal("0,4,0,0", (string?)line.Attribute("Margin"));
        Assert.Contains(line.Descendants(), e => (string?)e.Attribute("Text") == "{StaticResource Icon.Warning}");
        Assert.Contains(line.Descendants(Wpf + "TextBlock"), t => (string?)t.Attribute("Text") == "{TemplateBinding theme:Field.Error}" && (string?)t.Attribute("Style") == "{StaticResource Text.Error}");

        Assert.Contains(scope.Descendants(Wpf + "Trigger"), t => (string?)t.Attribute("Property") == "theme:Field.HasError");
        Assert.Contains(style.Descendants(Wpf + "Setter"), s => (string?)s.Attribute("Property") == "AutomationProperties.ItemStatus");
    }

    private static XElement Named(XElement scope, string name) => scope.Descendants().Single(e => (string?)e.Attribute(X + "Name") == name);

    private static XElement Components() => XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Theme", "Components.xaml")).Root!;
}
