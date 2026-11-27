using System.Xml.Linq;
using ScreenTrail.Core.Shell;

namespace ScreenTrail.Tests.Shell;

/// <summary>Review finding D-14: Windows' Text size scales the type, the line heights and the rows that hold text.</summary>
public sealed class TextScaleTests
{
    [Theory]
    [InlineData(null, 1.0)]
    [InlineData(100, 1.0)]
    [InlineData(125, 1.25)]
    [InlineData(150, 1.5)]
    [InlineData(225, 2.25)]
    [InlineData(99, 1.0)]
    [InlineData(300, 1.0)]
    [InlineData("150", 1.0)]
    public void TheFactorIsWhatWindowsStoredWithinItsRange(object? stored, double expected) =>
        Assert.Equal(expected, TextScale.Factor(stored));

    [Theory]
    [InlineData(14, 1.25, 18)]
    [InlineData(20, 1.25, 25)]
    [InlineData(11, 1.5, 17)]
    [InlineData(36, 1.0, 36)]
    public void ATokenLandsOnWholePixels(double token, double factor, double expected) =>
        Assert.Equal(expected, TextScale.Scaled(token, factor));

    [Fact]
    public void EveryScaledKeyIsAToken()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(folder.FullName, "ScreenTrail.UI")))
        {
            folder = folder.Parent!;
        }

        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var tokens = XDocument.Load(Path.Combine(folder.FullName, "ScreenTrail.UI", "Theme", "Tokens.Common.g.xaml"))
            .Descendants().Select(e => (string?)e.Attribute(x + "Key")).OfType<string>().ToHashSet();
        Assert.All(TextScale.Keys, key => Assert.Contains(key, tokens));
        Assert.All(tokens.Where(k => k.StartsWith("Size.", StringComparison.Ordinal) || k.StartsWith("Leading.", StringComparison.Ordinal)), key => Assert.Contains(key, TextScale.Keys));
    }
}
