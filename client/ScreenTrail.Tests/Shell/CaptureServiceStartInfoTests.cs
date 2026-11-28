using ScreenTrail.Core.Ipc;
using ScreenTrail.Core.Shell;

namespace ScreenTrail.Tests.Shell;

/// <summary>
/// How the window starts the capture service (review finding E-13; B-02). In the installer's build the
/// service it starts gets an environment with nothing the runtime reads before Main, and the runtime's
/// diagnostics off, so no profiler, replacement collector or diagnostics tool rides along with it and no
/// diagnostics pipe opens. A development build starts it with the environment as it is, so a developer can
/// still attach a profiler or a debugger.
/// </summary>
public sealed class CaptureServiceStartInfoTests
{
    private const string Service = "/Program Files/ScreenTrail/ScreenTrail.Service.exe";

    [Fact]
    public void TheInstallersBuildStartsTheServiceCleanWithDiagnosticsOff()
    {
        var start = CaptureServiceStart.StartInfo(Service, installerBuild: true);

        Assert.Equal("0", start.Environment[RunningHonestly.DiagnosticsSwitch]);
        Assert.DoesNotContain(start.Environment.Keys, name => !string.Equals(name, RunningHonestly.DiagnosticsSwitch, StringComparison.OrdinalIgnoreCase)
            && (name.StartsWith("DOTNET_", StringComparison.OrdinalIgnoreCase) || name.StartsWith("CORECLR_", StringComparison.OrdinalIgnoreCase) || name.StartsWith("COMPlus_", StringComparison.OrdinalIgnoreCase)));
        Assert.False(start.UseShellExecute);
        Assert.Equal(Service, start.FileName);
        Assert.Equal(Path.GetDirectoryName(Service), start.WorkingDirectory);
    }

    [Fact]
    public void ADevelopmentBuildStartsTheServiceWithTheEnvironmentAsItIs()
    {
        var start = CaptureServiceStart.StartInfo(Service, installerBuild: false);

        Assert.Equal(
            System.Environment.GetEnvironmentVariable(RunningHonestly.DiagnosticsSwitch),
            start.Environment.TryGetValue(RunningHonestly.DiagnosticsSwitch, out var diagnostics) ? diagnostics : null);
        Assert.Equal(System.Environment.GetEnvironmentVariables().Count, start.Environment.Count);
        Assert.False(start.UseShellExecute);
        Assert.Equal(Path.GetDirectoryName(Service), start.WorkingDirectory);
    }
}
