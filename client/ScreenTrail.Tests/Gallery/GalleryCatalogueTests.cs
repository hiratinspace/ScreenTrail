using System.Xml.Linq;
using ScreenTrail.Tests.Theme;
using ScreenTrail.UI.Gallery;

namespace ScreenTrail.Tests.Gallery;

/// <summary>
/// The gallery is the component reference (review finding B-56): a row for every button style and every
/// field style, each control in each state it has, and every badge and banner tone the templates draw.
/// Held against Components.xaml, so a new style or tone that the gallery does not show fails here.
/// </summary>
public sealed class GalleryCatalogueTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static HashSet<string> Keys() =>
        [.. ChoiceStatesTests.Components().Elements(Wpf + "Style").Select(s => (string?)s.Attribute(X + "Key")).OfType<string>()];

    [Fact]
    public void EveryRowNamesAStyleThatExists()
    {
        var keys = Keys();
        Assert.All(GalleryCatalogue.Rows.Where(r => r.StyleKey is not null), r => Assert.Contains(r.StyleKey!, keys));
    }

    [Fact]
    public void EveryButtonStyleAndFieldStyleHasARow()
    {
        var shown = GalleryCatalogue.Rows.Select(r => r.StyleKey).ToHashSet();
        var buttons = Keys().Where(k => k.StartsWith("Button.", StringComparison.Ordinal) && k != "Button.Base");
        Assert.All(buttons, k => Assert.Contains(k, shown));
        Assert.Contains("Input.Default", shown);
        Assert.Contains("Input.Mono", shown);
        Assert.Contains("Segment", shown);
        Assert.Contains("CheckChip", shown);
        foreach (var sample in Enum.GetValues<GallerySample>())
        {
            Assert.Contains(GalleryCatalogue.Rows, r => r.Sample == sample);
        }
    }

    [Fact]
    public void EachRowStartsAtRestAndShowsItsHoverAndItsFocus()
    {
        Assert.Equal(Enum.GetValues<GalleryState>(), GalleryCatalogue.Columns);
        Assert.All(GalleryCatalogue.Rows, r =>
        {
            Assert.Equal(GalleryState.Rest, r.States[0]);
            Assert.Contains(GalleryState.Hover, r.States);
            Assert.Contains(GalleryState.Focused, r.States);
            Assert.Equal(r.States.Distinct().Count(), r.States.Count);
        });

        // Loading is the primary button's (theme:Busy.Active), and an error line is a field's (theme:Field.Error).
        Assert.Equal(["Button.Primary"], GalleryCatalogue.Rows.Where(r => r.States.Contains(GalleryState.Loading)).Select(r => r.StyleKey));
        Assert.All(GalleryCatalogue.Rows.Where(r => r.States.Contains(GalleryState.Error)), r => Assert.Equal(GallerySample.Input, r.Sample));
        Assert.Contains(GalleryCatalogue.Rows, r => r.States.Contains(GalleryState.Error) && r.StyleKey == "Input.Mono");
    }

    [Theory]
    [InlineData("Badge", "theme:Badge.Tone", "Neutral")]
    [InlineData("Banner", "theme:Banner.Tone", "Info")]
    public void EveryToneTheTemplateDrawsIsShown(string style, string property, string resting)
    {
        var template = ChoiceStatesTests.Components().Elements(Wpf + "Style").Single(s => (string?)s.Attribute(X + "Key") == style);
        var drawn = template.Descendants().Where(e => (string?)e.Attribute("Property") == property).Select(e => (string)e.Attribute("Value")!).Append(resting).ToHashSet();
        var shown = style == "Badge" ? GalleryCatalogue.Badges.Select(b => b.Tone) : GalleryCatalogue.Banners.Select(b => b.Tone);
        Assert.Equal(drawn.Order(), shown.Order());
    }

    [Fact]
    public void TheGalleryHoldsNoCopyOfADialogAndNoDeletedStyle()
    {
        var gallery = EmptyStateTests.View("Gallery", "GalleryWindow.xaml");
        Assert.Single(gallery.Descendants(Wpf + "Grid"), g => (string?)g.Attribute(X + "Name") == "Matrix");
        // The dialog sample was a Primary destructive button over a plain field, unlike ConfirmWindow. The
        // dialogs have renders of their own, and every field here is the matrix's, built in code.
        Assert.Empty(gallery.Descendants(Wpf + "TextBox"));

        var keys = Keys();
        var local = gallery.Descendants(Wpf + "Style").Select(s => (string?)s.Attribute(X + "Key")).OfType<string>().ToHashSet();
        foreach (var used in gallery.Descendants().Attributes("Style").Select(a => a.Value).Where(v => v.StartsWith("{StaticResource ", StringComparison.Ordinal)))
        {
            var key = used["{StaticResource ".Length..^1];
            Assert.True(keys.Contains(key) || local.Contains(key), $"The gallery uses {key}, which Components.xaml does not define.");
        }
    }
}
