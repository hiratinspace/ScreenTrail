using System.Xml.Linq;

namespace ScreenTrail.Tests.Theme;

/// <summary>
/// Styles nothing in the product used are gone, so the theme holds only what is drawn (review findings
/// B-16, B-22): a style kept "for later" is never brought to spec and drifts from the rest.
/// </summary>
public sealed class DeadStylesTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Theory]
    [InlineData("Chip")]
    [InlineData("Nav.Item")]
    [InlineData("Pill.Control")]
    public void AnUnusedKeyedStyleIsGone(string key)
    {
        Assert.DoesNotContain(ChoiceStatesTests.Components().Elements(Wpf + "Style"), s => (string?)s.Attribute(X + "Key") == key);
    }

    [Theory]
    [InlineData("PasswordBox")]
    public void AnUnusedImplicitStyleIsGone(string targetType)
    {
        Assert.DoesNotContain(ChoiceStatesTests.Components().Elements(Wpf + "Style"), s => (string?)s.Attribute("TargetType") == targetType);
    }
}
