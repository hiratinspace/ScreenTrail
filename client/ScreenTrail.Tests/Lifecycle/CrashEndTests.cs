using ScreenTrail.Core.Lifecycle;

namespace ScreenTrail.Tests.Lifecycle;

/// <summary>
/// A-08: an exception nothing caught is reported (stack only, when opted in) and then the process ends
/// itself, on a path a hung or failing report cannot hold, before the runtime would write the exception's
/// message to the Application event log. The Windows calls are ProcessEnd's; the Windows suite runs them.
/// </summary>
public sealed class CrashEndTests
{
    [Fact(Timeout = 30_000)]
    public async Task TheReportIsWrittenAndThenTheProcessEnds()
    {
        var ct = TestContext.Current.CancellationToken;
        var said = new List<string>();
        var crash = new InvalidOperationException("MARKER");

        await Task.Run(() => CrashEnd.ReportThenEnd(crash, thrown => { lock (said) { said.Add("report " + thrown.Message); } }, code => { lock (said) { said.Add($"end {code}"); } }, TimeSpan.FromSeconds(20)), ct);

        Assert.Equal(["report MARKER", $"end {CrashEnd.ExitCode}"], said);
        Assert.NotEqual(0, CrashEnd.ExitCode);
    }

    [Fact(Timeout = 30_000)]
    public async Task AReportThatDoesNotFinishDoesNotHoldTheEnd()
    {
        var ct = TestContext.Current.CancellationToken;
        using var never = new ManualResetEventSlim(false);
        int? ended = null;
        try
        {
            await Task.Run(() => CrashEnd.ReportThenEnd(new InvalidOperationException(), _ => never.Wait(), code => ended = code, TimeSpan.FromMilliseconds(50)), ct);
        }
        finally
        {
            never.Set();
        }

        Assert.Equal(CrashEnd.ExitCode, ended);
    }

    [Fact(Timeout = 30_000)]
    public async Task AReportThatFailsStillEndsTheProcess()
    {
        var ct = TestContext.Current.CancellationToken;
        int? ended = null;

        await Task.Run(() => CrashEnd.ReportThenEnd(new InvalidOperationException(), _ => throw new IOException("disk full"), code => ended = code, TimeSpan.FromSeconds(20)), ct);

        Assert.Equal(CrashEnd.ExitCode, ended);
    }

    [Fact]
    public void SomethingThrownThatIsNotAnExceptionIsStillReported()
    {
        Assert.IsType<InvalidOperationException>(CrashEnd.Of("not an exception"));
        var crash = new FormatException();
        Assert.Same(crash, CrashEnd.Of(crash));
    }

    [Theory]
    [InlineData("ScreenTrail.Service", "Program.cs", "ProcessEnd.Install(WriteCrashReport)")]
    [InlineData("ScreenTrail.UI", "App.xaml.cs", "ProcessEnd.Install(Report)")]
    [InlineData("ScreenTrail.UI", "App.xaml.cs", "ProcessEnd.End(args.Exception, Report)")]
    public void TheServiceAndTheWindowEndThemselvesOnACrash(string project, string file, string call)
    {
        var source = File.ReadAllText(Path.Combine(Repository(), "client", project, file));

        Assert.Contains(call, source, StringComparison.Ordinal);
    }

    private static string Repository()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "client", "ScreenTrail.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Not inside the repository.");
    }
}
