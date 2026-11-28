using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace ScreenTrail.Api.Tests.Publish;

/// <summary>
/// A publish's time entry may start at most 365 days before the server's clock (E-02), so a test that
/// publishes with a fixed date passes until that date is a year old and then fails. Every publish test
/// works its start out from the clock instead (<c>PublishDates</c>), and this keeps a fixed one from
/// coming back: no test source sets <c>StartedAt</c> to a written date, and every test that posts the
/// shared publish contract replaces the contract's example date the same way.
/// </summary>
public sealed partial class PublishDatesGuardTests
{
    [GeneratedRegex(@"StartedAt\s*=\s*(new\s+DateTimeOffset\s*\(\s*\d{4}|DateTimeOffset\.Parse)")]
    private static partial Regex WrittenStart();

    [Fact]
    public void NoTestPublishesWithAWrittenDate()
    {
        var written = Sources()
            .SelectMany(file => File.ReadLines(file).Select((line, i) => (file, line, i)))
            .Where(x => WrittenStart().IsMatch(x.line))
            .Select(x => $"{Path.GetFileName(x.file)}:{x.i + 1}")
            .ToList();

        Assert.Empty(written);
    }

    [Fact]
    public void EveryTestThatPostsThePublishContractReplacesItsDate()
    {
        var unreplaced = Sources()
            .Where(file => !file.EndsWith(nameof(PublishDatesGuardTests) + ".cs", StringComparison.Ordinal))
            .Where(file => File.ReadAllText(file).Contains("publish-request.v1.json", StringComparison.Ordinal))
            .Where(file => !File.ReadAllText(file).Contains("PublishDates.", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();

        Assert.Empty(unreplaced);
    }

    /// <summary>The test project's own sources, found from this file's path at build time.</summary>
    private static IEnumerable<string> Sources([CallerFilePath] string here = "")
    {
        var root = Path.GetDirectoryName(Path.GetDirectoryName(here))!;
        Assert.True(File.Exists(Path.Combine(root, "ScreenTrail.Api.Tests.csproj")), root);
        return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
    }
}
