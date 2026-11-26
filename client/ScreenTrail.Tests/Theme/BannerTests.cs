using System.Xml.Linq;

namespace ScreenTrail.Tests.Theme;

/// <summary>
/// One banner with a tone and a glyph slot (design system 10.6; review findings A-11, A-12, B-26): every
/// screen used to improvise its own glyph, a Unicode sign in the text face or none at all.
/// </summary>
public sealed class BannerTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace Controls = "clr-namespace:ScreenTrail.UI.Controls";

    [Fact]
    public void EachToneDrawsItsTintASoftEdgeAndAnIconFontGlyph()
    {
        var banner = Banner();
        var tones = banner.Descendants(Wpf + "Trigger").Where(t => (string?)t.Attribute("Property") == "theme:Banner.Tone")
            .ToDictionary(t => (string)t.Attribute("Value")!, t => t.Elements(Wpf + "Setter").ToDictionary(s => (string)s.Attribute("TargetName")! + "." + (string)s.Attribute("Property")!, s => (string)s.Attribute("Value")!));

        Assert.Equal(["Danger", "Success", "Warning"], tones.Keys.Order());
        Assert.Equal("{DynamicResource Brush.border.warning}", tones["Warning"]["Chrome.BorderBrush"]);
        Assert.Equal("{DynamicResource Brush.border.success}", tones["Success"]["Chrome.BorderBrush"]);
        Assert.Equal("{DynamicResource Brush.border.danger}", tones["Danger"]["Chrome.BorderBrush"]);

        // Info: the info fill and a grey glyph, never the selection tint or Iris.
        Assert.Equal("{DynamicResource Brush.bg.info}", Setter(banner, "Background"));
        var glyph = banner.Descendants(Controls + "Glyph").Single(g => (string?)g.Attribute(X + "Name") == "Glyph");
        Assert.Equal("{DynamicResource Brush.text.secondary}", (string?)glyph.Attribute("Foreground"));
        Assert.Equal("{DynamicResource Icon.control}", (string?)glyph.Attribute("FontSize"));
        Assert.Equal("0,2,8,0", (string?)glyph.Attribute("Margin"));
        Assert.DoesNotContain(banner.Descendants().Attributes(), a => a.Value.Contains("accent", StringComparison.Ordinal) || a.Value.Contains("bg.selected", StringComparison.Ordinal));
    }

    [Fact]
    public void TheBorderBannersAreGone()
    {
        var components = ChoiceStatesTests.Components();
        foreach (var retired in new[] { "Banner.Info", "Banner.Warning", "Banner.Error" })
        {
            Assert.DoesNotContain(components.Elements(Wpf + "Style"), s => (string?)s.Attribute(X + "Key") == retired);
        }
    }

    [Fact]
    public void NoViewDrawsAWarningOrInfoSignInTheTextFace()
    {
        // A Unicode sign renders in Segoe UI at the wrong weight beside Fluent glyphs (review finding A-12).
        var ui = Path.Combine(Root(), "client", "ScreenTrail.UI");
        var offenders = Directory.EnumerateFiles(ui, "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => File.ReadAllText(f).Contains("Text=\"⚠\"", StringComparison.Ordinal) || File.ReadAllText(f).Contains("Text=\"ⓘ\"", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(offenders);
    }

    private static string? Setter(XElement style, string property) =>
        (string?)style.Elements(Wpf + "Setter").Single(s => (string?)s.Attribute("Property") == property).Attribute("Value");

    private static XElement Banner() => ChoiceStatesTests.Components().Elements(Wpf + "Style").Single(s => (string?)s.Attribute(X + "Key") == "Banner");

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(directory!.FullName, "client", "ScreenTrail.UI")))
        {
            directory = directory.Parent;
        }

        return directory.FullName;
    }
}
