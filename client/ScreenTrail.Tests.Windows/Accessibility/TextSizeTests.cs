using System.Runtime.Versioning;
using System.Windows;
using ScreenTrail.UI.Theme;

namespace ScreenTrail.Tests.Windows.Accessibility;

/// <summary>Review finding D-14: Windows' Text size reaches the type tokens, and 100% takes the overrides away.</summary>
[SupportedOSPlatform("windows")]
[Collection(WpfApplicationOwner.Name)]
public sealed class TextSizeTests(TheApplication app)
{
    [Fact]
    public async Task TheTokensFollowTextSize()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only.");
        await app.RunAsync(Check);
    }

    private static void Check()
    {
        var resources = new ResourceDictionary();
        resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/ScreenTrail.UI;component/Theme/Tokens.Common.g.xaml") });

        ThemeManager.ApplyTextSize(resources, 150);
        Assert.Equal(21d, resources["Size.md"]);
        Assert.Equal(30d, resources["Leading.body"]);
        Assert.Equal(54d, resources["Layout.row-height"]);

        ThemeManager.ApplyTextSize(resources, null);
        Assert.False(resources.Contains("Size.md"));
        Assert.Equal(14d, resources["Size.md"]);
    }
}
