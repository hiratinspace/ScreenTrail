using System.Xml.Linq;
using ScreenTrail.Tests.Theme;

namespace ScreenTrail.Tests.Shell;

/// <summary>
/// The dialog, the shortcut sheet and the diagnostics panel, from the premium review (ST-141): what each is
/// drawn with and how the keyboard reaches it.
/// </summary>
public sealed class DialogPolishTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static XElement Dialog() => EmptyStateTests.View("Shell", "ConfirmWindow.xaml");

    private static XElement Named(XElement view, string name) =>
        view.Descendants().Single(e => (string?)e.Attribute(Xaml + "Name") == name);

    /// <summary>
    /// Review finding B-50: the question is on the surface with 24 around it, and the buttons on a footer of
    /// their own, bg.base under a border.subtle rule, 24,16, right aligned 8 apart.
    /// </summary>
    [Fact]
    public void TheDialogIsASurfaceOverAFooter()
    {
        var dialog = Dialog();
        Assert.Equal("{DynamicResource Brush.bg.surface}", (string?)Named(dialog, "Body").Attribute("Background"));
        Assert.Equal("{DynamicResource Padding.pane}", (string?)Named(dialog, "Body").Elements().Single().Attribute("Margin"));

        var footer = Named(dialog, "Footer");
        Assert.Equal("{DynamicResource Brush.bg.base}", (string?)footer.Attribute("Background"));
        Assert.Equal("{DynamicResource Brush.border.subtle}", (string?)footer.Attribute("BorderBrush"));
        Assert.Equal("0,1,0,0", (string?)footer.Attribute("BorderThickness"));
        Assert.Equal("{DynamicResource Padding.dialog-footer}", (string?)footer.Attribute("Padding"));
        var buttons = footer.Elements(Wpf + "StackPanel").Single();
        Assert.Equal("Right", (string?)buttons.Attribute("HorizontalAlignment"));
        Assert.Equal(2, buttons.Elements(Wpf + "Button").Count());
        Assert.Equal("8,0,0,0", (string?)Named(dialog, "Confirm").Attribute("Margin"));
    }
}
