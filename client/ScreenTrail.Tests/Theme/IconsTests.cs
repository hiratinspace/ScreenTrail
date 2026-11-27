using System.Xml.Linq;

namespace ScreenTrail.Tests.Theme;

/// <summary>
/// Every icon is named by concept in Theme/Icons.xaml, and no view types a codepoint (design system 8, review
/// finding A-29). Whether both icon fonts hold each one is the Windows suite's question.
/// </summary>
public sealed class IconsTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void EveryIconIsOneConceptAndOnePrivateUseCodepoint()
    {
        var icons = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Theme", "Icons.xaml")).Root!.Elements().ToList();

        Assert.NotEmpty(icons);
        Assert.All(icons, i => Assert.StartsWith("Icon.", (string)i.Attribute(X + "Key")!, StringComparison.Ordinal));
        Assert.All(icons, i => Assert.True(i.Value.Length == 1 && i.Value[0] is >= '' and <= '', $"{i.Attribute(X + "Key")} is not one glyph"));
        Assert.Equal(icons.Count, icons.Select(i => i.Value).Distinct().Count());
    }

    [Fact]
    public void NoViewTypesACodepoint()
    {
        var ui = Path.Combine(Repo(), "client", "ScreenTrail.UI");
        var typed = Directory.EnumerateFiles(ui, "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith("Icons.xaml", StringComparison.Ordinal) && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(f => XDocument.Load(f).Descendants().Attributes().Where(a => a.Value.Any(c => c is >= '' and <= '')).Select(a => $"{Path.GetFileName(f)} {a.Name.LocalName}"))
            .ToList();

        Assert.Empty(typed);
    }

    private static string Repo()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "client", "ScreenTrail.UI")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("client/ScreenTrail.UI");
    }
}
