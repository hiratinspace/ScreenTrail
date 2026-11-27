using System.Xml.Linq;
using ScreenTrail.Core.Notifications;

namespace ScreenTrail.Tests.Notifications;

/// <summary>Review findings C-T6 and D-10: a notice as a real toast, titled by its event, with one named button.</summary>
public sealed class ToastContentTests
{
    [Fact]
    public void ADraftReadyToastHasItsEventItsWordsAndOneButton()
    {
        var notice = new Notification("Acme, ticket 4512. Select to open it.", NotificationAction.Review, "draft-ready:s1", "Draft ready to check");

        var toast = XElement.Parse(ToastContent.Xml(notice));

        Assert.Equal("action=Review", (string?)toast.Attribute("launch"));
        Assert.Equal(["Draft ready to check", "Acme, ticket 4512. Select to open it."], toast.Descendants("text").Select(t => t.Value));
        Assert.Equal("ToastGeneric", (string?)toast.Descendants("binding").Single().Attribute("template"));
        var button = Assert.Single(toast.Descendants("action"));
        Assert.Equal("Open draft", (string?)button.Attribute("content"));
        Assert.Equal("action=Review", (string?)button.Attribute("arguments"));
        Assert.Equal("foreground", (string?)button.Attribute("activationType"));
    }

    [Fact]
    public void ANoticeWithNothingToPressHasNoButton()
    {
        var toast = XElement.Parse(ToastContent.Xml(new Notification("ScreenTrail 1.2 is ready.", NotificationAction.None, "update-ready:1.2", "Update ready")));
        Assert.Empty(toast.Descendants("actions"));
    }

    [Fact]
    public void WordsAreEscapedNotTrusted()
    {
        var toast = XElement.Parse(ToastContent.Xml(new Notification("<b>Fish & Chips</b> \"Ltd\"", NotificationAction.Open, "k", "A & B")));
        Assert.Equal("<b>Fish & Chips</b> \"Ltd\"", toast.Descendants("text").Last().Value);
        Assert.Equal("A & B", toast.Descendants("text").First().Value);
    }

    [Fact]
    public void TheTagIsTheKeyAndFitsWindowsLimit()
    {
        Assert.Equal("draft-ready:s1", ToastContent.Tag(new Notification("t", NotificationAction.None, "draft-ready:s1")));
        var tag = ToastContent.Tag(new Notification("t", NotificationAction.None, new string('k', 200)));
        Assert.Equal(ToastContent.MaxTag, tag.Length);
        Assert.Equal(tag, ToastContent.Tag(new Notification("u", NotificationAction.None, new string('k', 200))));
    }

    [Theory]
    [InlineData("action=Review", NotificationAction.Review)]
    [InlineData("action=Open", NotificationAction.Open)]
    [InlineData("action=None", NotificationAction.None)]
    [InlineData("action=7", NotificationAction.None)]
    [InlineData("action=Delete", NotificationAction.None)]
    [InlineData("", NotificationAction.None)]
    [InlineData(null, NotificationAction.None)]
    public void APressedToastSaysOnlyAnActionItKnows(string? arguments, NotificationAction expected) =>
        Assert.Equal(expected, ToastContent.ActionOf(arguments));

    [Fact]
    public void EveryButtonIsPlainWords()
    {
        foreach (var action in Enum.GetValues<NotificationAction>())
        {
            if (ToastContent.ButtonFor(action) is { } words)
            {
                Assert.DoesNotContain('—', words);
                Assert.DoesNotContain('·', words);
            }
        }
    }
}
