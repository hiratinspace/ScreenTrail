using System.Diagnostics;
using System.Runtime.Versioning;
using ScreenTrail.Core.Ipc;
using ScreenTrail.Core.Shell;

namespace ScreenTrail.Tests.Windows.Shell;

/// <summary>
/// B-02 (c): the capture service the window starts in the installer's build sees none of the runtime's
/// settings and has diagnostics off, read back from a real child process's environment block. A child
/// that is not .NET (cmd's <c>set</c>) is used, so a planted name can do nothing but be printed. No desktop
/// is needed, so this runs on the hosted runner.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CaptureServiceCleanStartTests
{
    private static readonly string Shell = Path.Combine(Environment.SystemDirectory, "cmd.exe");

    [Fact]
    public void TheServiceTheWindowStartsInTheInstallersBuildHasDiagnosticsOffAndNoRuntimeSetting()
    {
        var start = CaptureServiceStart.StartInfo(Shell, installerBuild: true);

        var seen = Run(start);

        Assert.Contains("DOTNET_EnableDiagnostics=0", seen);
        Assert.DoesNotContain(seen, line => RuntimeSetting(line) && !line.Equals("DOTNET_EnableDiagnostics=0", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void NamesSetForTheChildAreRemovedWhateverTheirCase()
    {
        var start = new ProcessStartInfo(Shell) { UseShellExecute = false };
        foreach (var name in new[] { "CORECLR_ENABLE_PROFILING", "coreclr_profiler", "CORECLR_PROFILER_PATH_64", "CORECLR_ENABLE_NOTIFICATION_PROFILERS", "CORECLR_NOTIFICATION_PROFILERS", "DOTNET_GCName", "complus_gcpath", "DOTNET_DiagnosticPorts" })
        {
            start.Environment[name] = "planted";
        }

        RunningHonestly.StartClean(start.Environment);
        var seen = Run(start);

        Assert.DoesNotContain(seen, line => line.Contains("planted", StringComparison.Ordinal));
        Assert.Contains("DOTNET_EnableDiagnostics=0", seen);
    }

    private static bool RuntimeSetting(string line) =>
        line.StartsWith("DOTNET_", StringComparison.OrdinalIgnoreCase)
        || line.StartsWith("CORECLR_", StringComparison.OrdinalIgnoreCase)
        || line.StartsWith("COMPlus_", StringComparison.OrdinalIgnoreCase);

    private static List<string> Run(ProcessStartInfo start)
    {
        start.Arguments = "/d /c set";
        start.RedirectStandardOutput = true;
        start.CreateNoWindow = true;
        using var child = Process.Start(start) ?? throw new InvalidOperationException("cmd did not start");
        var output = child.StandardOutput.ReadToEnd();
        Assert.True(child.WaitForExit(30_000), "cmd's set ended");
        return [.. output.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0)];
    }
}
