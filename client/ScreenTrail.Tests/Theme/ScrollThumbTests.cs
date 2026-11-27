using System.Xml.Linq;

namespace ScreenTrail.Tests.Theme;

/// <summary>A quiet scroll thumb that grows under the pointer (design system 10.8, review findings A-33, B-29).</summary>
public sealed class ScrollThumbTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace Sys = "clr-namespace:System;assembly=System.Runtime";

    [Theory]
    [InlineData("ScrollBar.Thumb", "Width", "MinHeight")]
    [InlineData("ScrollBar.Thumb.Horizontal", "Height", "MinWidth")]
    public void TheThumbIsAThinPillThatWidensOverItsTrack(string key, string across, string along)
    {
        var thumb = ChoiceStatesTests.Components().Elements(Wpf + "Style").Single(s => (string?)s.Attribute(X + "Key") == key);
        var bar = thumb.Descendants(Wpf + "Border").Single(b => (string?)b.Attribute(X + "Name") == "Bar");

        Assert.Equal("{DynamicResource ScrollThumb.rest}", (string?)bar.Attribute(across));
        Assert.Equal("{DynamicResource Radius.pill}", (string?)bar.Attribute("CornerRadius"));
        Assert.Contains(thumb.Elements(Wpf + "Setter"), s => (string?)s.Attribute("Property") == along && (string?)s.Attribute("Value") == "{DynamicResource Layout.scroll-thumb-min}");

        var over = thumb.Descendants(Wpf + "DataTrigger").Single();
        Assert.Contains("AncestorType=ScrollBar", (string?)over.Attribute("Binding"), StringComparison.Ordinal);
        Assert.Contains(over.Elements(Wpf + "Setter"), s => (string?)s.Attribute("Property") == across && (string?)s.Attribute("Value") == "{DynamicResource Layout.scroll-thumb-hover}");
    }

    [Theory]
    [InlineData("Tokens.Light.g.xaml", "4")]
    [InlineData("Tokens.Dark.g.xaml", "4")]
    [InlineData("Tokens.HighContrast.xaml", "8")]
    public void HighContrastKeepsTheThumbWide(string file, string width)
    {
        var root = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Theme", file)).Root!;
        Assert.Equal(width, root.Elements(Sys + "Double").Single(d => (string?)d.Attribute(X + "Key") == "ScrollThumb.rest").Value);
    }
}
