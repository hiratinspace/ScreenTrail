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

    /// <summary>
    /// Review finding C-O6: the test card has one filled button per state (Start, then Stop and draft), and
    /// after the test "Run the test again" is Secondary so Continue is the one filled button; running shows
    /// the dot, "Recording" and the timer, done shows the outcome with its check.
    /// </summary>
    [Fact]
    public void TheTestCardHasOneFilledButtonPerState()
    {
        var card = Named(Window(), "TestCard");
        Assert.Equal("{StaticResource Button.Primary}", (string?)Named(card, "StartTest").Attribute("Style"));
        Assert.Equal("{StaticResource Button.Primary}", (string?)Named(card, "StopTest").Attribute("Style"));
        Assert.Equal("{StaticResource Button.Secondary}", (string?)Named(card, "AgainTest").Attribute("Style"));
        Assert.Equal(["{Binding StartTestVisibility}", "{Binding StopTestVisibility}", "{Binding AgainTestVisibility}"], card.Descendants(Wpf + "Button").Select(b => (string?)b.Attribute("Visibility")));

        var running = Named(card, "TestRunningLine");
        Assert.Contains(running.Elements(Wpf + "Ellipse"), e => (string?)e.Attribute("Style") == "{StaticResource StateDot}");
        Assert.Contains(running.Elements(Wpf + "TextBlock"), t => (string?)t.Attribute("Style") == "{StaticResource Text.Numeric}" && (string?)t.Attribute("Text") == "{Binding TestElapsed}");
        Assert.Contains(Named(card, "TestDoneLine").Descendants(), e => (string?)e.Attribute("Text") == "{StaticResource Icon.Check}");
    }

    /// <summary>
    /// Review finding C-O2: setup pictures the pill the technician will look for, drawn with the real pill's
    /// frame (HudWindow's Pill), so the two cannot drift apart; nothing in the picture is a control.
    /// </summary>
    [Fact]
    public void ThePillPictureHasTheRealPillsFrame()
    {
        var real = Named(EmptyStateTests.View("Hud", "HudWindow.xaml"), "Pill");
        var template = EmptyStateTests.View("Theme", "Components.xaml").Descendants(Wpf + "ControlTemplate").Single(t => (string?)t.Attribute(Xaml + "Key") == "Pill.Picture");
        var picture = Named(template, "Pill");
        foreach (var property in new[] { "Height", "MinWidth", "CornerRadius", "Background", "BorderBrush", "BorderThickness", "Padding" })
        {
            Assert.Equal((string?)real.Attribute(property), (string?)picture.Attribute(property));
        }

        Assert.Equal("False", (string?)picture.Attribute("IsHitTestVisible"));
        Assert.Empty(template.Descendants(Wpf + "Button"));
        Assert.Empty(template.Descendants(Wpf + "TextBlock"));
    }

    /// <summary>
    /// Review finding C-O2: Done pictures the pill under "Look for the pill", with what it is for; Welcome
    /// shows the three parts (the pill, a drafted note, Publish) each with its line.
    /// </summary>
    [Fact]
    public void DoneAndWelcomePictureWhatTheTechnicianWillSee()
    {
        var window = Window();
        var done = Named(window, "DoneStep");
        Assert.Equal("{StaticResource Pill.Picture}", (string?)Named(done, "DonePill").Attribute("Template"));
        Assert.Contains(done.Elements(Wpf + "TextBlock"), t => ((string?)t.Attribute("Text"))?.Contains("screen share", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(done.Descendants(), e => e.Attribute("FontSize") is not null);

        var parts = Named(window, "WelcomeParts");
        var captions = parts.Descendants(Wpf + "TextBlock").Where(t => (string?)t.Attribute("Style") == "{StaticResource Text.Caption}").Select(t => (string?)t.Attribute("Text")).ToList();
        Assert.Equal(3, captions.Count);
        Assert.All(captions, c => Assert.DoesNotContain('\u2014', c!));
        Assert.Contains(parts.Descendants(Wpf + "Control"), c => (string?)c.Attribute("Template") == "{StaticResource Pill.Picture}");
        Assert.Empty(parts.Descendants(Wpf + "Button"));
    }

    /// <summary>
    /// Review finding D-30: a new step opens at its top and fades in over motion.state, and not at all with
    /// reduce motion on. The window's code is read as text: the Mac cannot run a WPF window.
    /// </summary>
    [Fact]
    public void ANewStepOpensAtItsTopAndFadesIn()
    {
        var window = Window();
        Assert.Equal("ScrollViewer", Named(window, "StepScroller").Name.LocalName);
        Assert.Equal("Grid", Named(window, "StepContent").Name.LocalName);

        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(folder.FullName, "ScreenTrail.UI")))
        {
            folder = folder.Parent!;
        }

        var code = File.ReadAllText(Path.Combine(folder.FullName, "ScreenTrail.UI", "Onboarding", "OnboardingWindow.xaml.cs"));
        Assert.Contains("nameof(OnboardingViewModel.Step)", code, StringComparison.Ordinal);
        Assert.Contains("StepScroller.ScrollToTop()", code, StringComparison.Ordinal);
        Assert.Contains("ThemeManager.ReduceMotion", code, StringComparison.Ordinal);
        Assert.Contains("Waiting.Fade", code, StringComparison.Ordinal);
    }
}
