using System.Xml.Linq;
using ScreenTrail.Tests.Theme;

namespace ScreenTrail.Tests.Onboarding;

/// <summary>Onboarding and the first run, from the premium review (ST-142): what each step is drawn with and says.</summary>
public sealed class OnboardingPolishTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static XElement Window() => EmptyStateTests.View("Onboarding", "OnboardingWindow.xaml");

    private static XElement Named(XElement view, string name) =>
        view.Descendants().Single(e => (string?)e.Attribute(Xaml + "Name") == name);

    /// <summary>
    /// Review finding C-O8: the step heading is Display, 28/36, and the Welcome lead is Body with no size of
    /// its own; the banner under it is the Info banner with the lock in text.secondary.
    /// </summary>
    [Fact]
    public void TheStepHeadingIsDisplayAndTheLeadHasNoLocalSize()
    {
        var window = Window();
        Assert.Equal("{StaticResource Text.Display}", (string?)Named(window, "StepTitle").Attribute("Style"));
        var lead = Named(window, "WelcomeLead");
        Assert.Equal("{StaticResource Text.Body}", (string?)lead.Attribute("Style"));
        Assert.Null(lead.Attribute("FontSize"));
        var banner = lead.ElementsAfterSelf().First();
        Assert.Equal("{StaticResource Banner}", (string?)banner.Attribute("Style"));
        Assert.Null(banner.Attribute(XName.Get("Banner.Tone", "clr-namespace:ScreenTrail.UI.Theme")));
        Assert.Equal("{StaticResource Icon.Lock}", (string?)banner.Attribute(XName.Get("Banner.Glyph", "clr-namespace:ScreenTrail.UI.Theme")));
    }

    /// <summary>Review finding C-O7: Back is Secondary and hidden where it cannot act.</summary>
    [Fact]
    public void BackIsSecondaryAndOnlyThereWhenItCanAct()
    {
        var back = Named(Window(), "BackButton");
        Assert.Equal("{StaticResource Button.Secondary}", (string?)back.Attribute("Style"));
        Assert.Equal("{Binding BackVisibility}", (string?)back.Attribute("Visibility"));
        Assert.Null(back.Attribute("IsEnabled"));
    }

    /// <summary>
    /// Review finding C-O4: the permissions are one grouped card of divided rows, each led by a 16 px glyph
    /// with no disc: the check, the warning for the microphone, the close glyph for what capture needs. The
    /// consequence is a Caption under the row's message.
    /// </summary>
    [Fact]
    public void PermissionsAreOneGroupedCardOfGlyphRows()
    {
        XNamespace controls = "clr-namespace:ScreenTrail.UI.Controls";
        var window = Window();
        var card = Named(window, "PermissionsCard");
        Assert.Equal("{StaticResource Card.Group}", (string?)card.Attribute("Style"));
        var row = card.Descendants(Wpf + "DataTemplate").Single().Elements().Single();
        Assert.Equal("{StaticResource Card.GroupRow}", (string?)row.Attribute("Style"));
        Assert.Empty(row.Descendants(Wpf + "Ellipse"));
        Assert.Equal("{StaticResource Onboarding.Mark}", (string?)row.Descendants(controls + "Glyph").Single().Attribute("Style"));
        Assert.Contains(row.Descendants(Wpf + "TextBlock"), t => (string?)t.Attribute("Text") == "{Binding Consequence}" && (string?)t.Attribute("Style") == "{StaticResource Text.Caption}");

        var mark = window.Descendants(Wpf + "Style").Single(s => (string?)s.Attribute(Xaml + "Key") == "Onboarding.Mark");
        var triggers = mark.Descendants(Wpf + "DataTrigger").Select(t => (string?)t.Attribute("Value")).ToList();
        Assert.Equal(["{x:Static core:PermissionSeverity.Ok}", "{x:Static core:PermissionSeverity.OptionalMissing}"], triggers);
        Assert.Contains(mark.Elements(Wpf + "Setter"), s => (string?)s.Attribute("Value") == "{DynamicResource Brush.state.danger}");

        var components = EmptyStateTests.View("Theme", "Components.xaml");
        var group = components.Descendants(Wpf + "Style").Single(s => (string?)s.Attribute(Xaml + "Key") == "Card.GroupRow");
        Assert.Contains(group.Elements(Wpf + "Setter"), s => (string?)s.Attribute("Value") == "{DynamicResource Layout.group-row-height}");
    }
}
