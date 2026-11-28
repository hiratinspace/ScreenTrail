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

    /// <summary>
    /// A source path recorded at build time is not where the sources are when the build maps its paths
    /// (CI's deterministic build records <c>/_/</c>), so a test that finds files through it passes on a
    /// developer's machine and fails on CI. Tests find their files from the folder they run in instead.
    /// </summary>
    [Fact]
    public void NoTestFindsFilesThroughItsBuildTimePath()
    {
        var files = Sources();
        Assert.Contains(files, f => Path.GetFileName(f) == nameof(PublishDatesGuardTests) + ".cs");

        var offenders = files
            .Where(f => BuildTimePath().IsMatch(File.ReadAllText(f)))
            .Select(Path.GetFileName)
            .ToList();

        Assert.Empty(offenders);
    }

    [GeneratedRegex(@"[\[,]\s*(System\.Runtime\.CompilerServices\.)?CallerFilePath(Attribute)?\b")]
    private static partial Regex BuildTimePath();

    /// <summary>The test project's folder, found by walking up from the folder the tests run in.</summary>
    private static string ProjectFolder()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ScreenTrail.Api.Tests.csproj")))
            {
                return directory.FullName;
            }
        }

        // Failed rather than passed over no files: a guard that reads nothing guards nothing.
        throw new DirectoryNotFoundException("ScreenTrail.Api.Tests.csproj was not found above the test's folder.");
    }

    /// <summary>
    /// The test project's own sources. Never empty without failing: they must hold the publish tests these
    /// guards are known to check.
    /// </summary>
    private static List<string> Sources()
    {
        List<string> files = [.. Directory.EnumerateFiles(ProjectFolder(), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))];
        Assert.Contains(files, f => Path.GetFileName(f) == "PublishEndpointTests.cs");
        Assert.Contains(files, f => Path.GetFileName(f) == "KbPublishTests.cs");
        return files;
    }
}
