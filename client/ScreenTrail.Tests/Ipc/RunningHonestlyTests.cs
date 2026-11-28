using ScreenTrail.Core.Ipc;

namespace ScreenTrail.Tests.Ipc;

/// <summary>
/// A signed process that is running somebody else's code (ST-012; 2026-09-19 review).
///
/// The peer check verifies the file a process was started from, which is worth having and is not enough
/// on its own: .NET will load another assembly into a perfectly genuine, perfectly signed process if the
/// environment asks it to. <c>DOTNET_STARTUP_HOOKS</c> runs an initialiser before <c>Main</c>, and the
/// profiler pair loads a native DLL with full access to the runtime.
///
/// So an attacker needs no forged binary at all: launch our real, signed UI with a hook set, and it
/// passes the signature check for the same reason the genuine one does, while somebody else's code is
/// already running inside it, holding the session token. The defence is that the code being hijacked is
/// ours, so it can refuse.
/// </summary>
public sealed class RunningHonestlyTests
{
    [Theory]
    [InlineData("DOTNET_STARTUP_HOOKS")]
    [InlineData("CORECLR_ENABLE_PROFILING")]
    [InlineData("CORECLR_PROFILER_PATH")]
    public void ASignedBuildWillNotStartAlongsideSomebodyElsesCode(string variable)
    {
        var refusal = RunningHonestly.WhyNotToStart(development: false, Set(variable, "C:\\somewhere\\hook.dll"));

        Assert.NotNull(refusal);
        Assert.Contains(variable, refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRefusalNamesTheVariableAndNotItsValue()
    {
        // A path somebody chose has no business in a message a customer may be reading over a
        // technician's shoulder, and the name is the whole of what anyone needs to act (INV-10).
        var refusal = RunningHonestly.WhyNotToStart(development: false, Set("DOTNET_STARTUP_HOOKS", "C:\\evil\\payload.dll"));

        Assert.NotNull(refusal);
        Assert.DoesNotContain("payload.dll", refusal, StringComparison.Ordinal);
        Assert.DoesNotContain("evil", refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRefusalSaysWhatToDoAboutIt()
    {
        // Spec §4: what happened, then what to do. "Refused to start" with no next step is a support call.
        var refusal = RunningHonestly.WhyNotToStart(development: false, Set("DOTNET_STARTUP_HOOKS", "hook.dll"));

        Assert.Contains("Unset it", refusal!, StringComparison.Ordinal);
    }

    [Fact]
    public void ADevelopmentBuildStillStarts()
    {
        // Nobody signed it, and this is how a profiler is attached. A rule that makes debugging
        // impossible is a rule somebody deletes, and then it protects nothing at all.
        Assert.Null(RunningHonestly.WhyNotToStart(development: true, Set("DOTNET_STARTUP_HOOKS", "hook.dll")));
    }

    [Fact]
    public void ADevelopmentBuildStillSaysSo()
    {
        // Not silent, though: whoever is debugging should know their profiler is loaded, and whoever is
        // reading a bug report should be able to tell that this process was not running alone.
        Assert.Equal(
            ["DOTNET_STARTUP_HOOKS"],
            RunningHonestly.ForeignCodeRequested(Set("DOTNET_STARTUP_HOOKS", "hook.dll")));
    }

    [Fact]
    public void AnOrdinaryProcessStartsWithNothingToSay()
    {
        Assert.Null(RunningHonestly.WhyNotToStart(development: false, _ => null));
        Assert.Empty(RunningHonestly.ForeignCodeRequested(_ => null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AVariableSetToNothingIsNotSet(string value) =>
        Assert.Null(RunningHonestly.WhyNotToStart(development: false, Set("DOTNET_STARTUP_HOOKS", value)));

    [Fact]
    public void EveryOneThatIsSetIsNamed()
    {
        var refusal = RunningHonestly.WhyNotToStart(
            development: false,
            name => name is "DOTNET_STARTUP_HOOKS" or "CORECLR_PROFILER_PATH" ? "something" : null);

        Assert.Contains("DOTNET_STARTUP_HOOKS", refusal!, StringComparison.Ordinal);
        Assert.Contains("CORECLR_PROFILER_PATH", refusal, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("CORECLR_ENABLE_NOTIFICATION_PROFILERS")]
    [InlineData("CORECLR_NOTIFICATION_PROFILERS")]
    [InlineData("DOTNET_GCPath")]
    [InlineData("COMPlus_GCPath")]
    [InlineData("DOTNET_GCName")]
    [InlineData("COMPlus_GCName")]
    [InlineData("DOTNET_DiagnosticPorts")]
    [InlineData("COMPlus_DiagnosticPorts")]
    public void AShippedBuildAlsoRefusesTheOtherWaysTheRuntimeLoadsCodeOrATool(string variable)
    {
        // B-02: notification profilers load without CORECLR_ENABLE_PROFILING, a replacement garbage
        // collector is a native library the runtime loads, and a diagnostic port connects the process to a
        // tool at start. Each is honoured before Main, each under either spelling.
        var refusal = RunningHonestly.WhyNotToStart(development: false, Set(variable, "C:\\somewhere\\x.dll"));

        Assert.NotNull(refusal);
        Assert.Contains(variable, refusal, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, true, false)]
    [InlineData(true, false, false)]
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]
    public void OnlyABuildThatIsNotTheInstallersAndCarriesNoSignatureIsADevelopmentBuild(bool installerBuild, bool carriesNoSignature, bool development)
    {
        // B-02: every build is unsigned until the certificate exists, so a rule keyed on the signature alone
        // made the installed product a development build. The installer's build is compiled in.
        Assert.Equal(development, RunningHonestly.IsDevelopment(installerBuild, carriesNoSignature));
    }

    [Fact]
    public void TheInstallersBuildRefusesWhateverItsSignature()
    {
        var refusal = RunningHonestly.WhyNotToStart(
            RunningHonestly.IsDevelopment(installerBuild: true, carriesNoSignature: true),
            Set("CORECLR_ENABLE_PROFILING", "1"));

        Assert.NotNull(refusal);
    }

    [Fact]
    public void AChildStartedCleanHasNoRuntimeSettingAndDiagnosticsOff()
    {
        // B-02 (c): what the UI gives the capture service it starts in the installer's build. Every name the
        // runtime reads before Main goes, whatever its case; the rest of the environment stays.
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["CORECLR_ENABLE_PROFILING"] = "1",
            ["CORECLR_PROFILER_PATH_64"] = "C:\\x.dll",
            ["coreclr_notification_profilers"] = "C:\\y.dll",
            ["DOTNET_GCName"] = "z.dll",
            ["DOTNET_EnableDiagnostics"] = "1",
            ["COMPlus_EnableDiagnostics"] = "1",
            ["DOTNET_ROOT"] = "C:\\dotnet",
            ["SCREENTRAIL_LOG_LEVEL"] = "Debug",
            ["PATH"] = "C:\\Windows",
        };

        RunningHonestly.StartClean(environment);

        Assert.Equal("0", environment[RunningHonestly.DiagnosticsSwitch]);
        Assert.Equal("DOTNET_EnableDiagnostics", RunningHonestly.DiagnosticsSwitch);
        Assert.Equal(
            ["DOTNET_EnableDiagnostics", "PATH", "SCREENTRAIL_LOG_LEVEL"],
            environment.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.Empty(RunningHonestly.ForeignCodeRequested(name => environment.TryGetValue(name, out var value) ? value : null));
    }

    private static Func<string, string?> Set(string variable, string value) =>
        name => string.Equals(name, variable, StringComparison.Ordinal) ? value : null;
}
