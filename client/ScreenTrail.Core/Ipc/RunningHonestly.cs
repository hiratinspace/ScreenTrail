namespace ScreenTrail.Core.Ipc;

/// <summary>
/// Whether this process is running its own code and nothing else (ST-012).
///
/// The peer check verifies the file a process was started from. That is worth having and it is not
/// enough on its own, because .NET will load somebody else's code into a perfectly genuine, perfectly
/// signed process if the environment asks it to. <c>DOTNET_STARTUP_HOOKS</c> names assemblies whose
/// initialiser runs before <c>Main</c>; it exists for profilers and it is a documented way to run
/// arbitrary code inside a signed executable.
///
/// So an attacker needs no forged binary at all. Launch our real, signed UI with a hook set, and it
/// passes the signature check for the same reason the genuine one does, while somebody else's code is
/// already running inside it, holding the session token (2026-09-19 review).
///
/// <b>The defence is that the code being hijacked refuses to run.</b> The hijacked process is our code,
/// so our code gets to say no. A development build says so out loud rather than refusing, because these
/// variables are how a profiler is attached and a build nobody ships should stay debuggable.
///
/// What this cannot do: the runtime acts on these variables before <c>Main</c>, so a profiler, collector
/// or tool it loaded has already run by the time this reads them, and one that removes its own variables
/// from the process before then is not seen at all. The refusal stops a process that was started this way
/// from going on as ScreenTrail; it is not what keeps such code out (ADR-0003). <see cref="StartClean"/> is
/// the part that runs ahead of the runtime, for a process ScreenTrail itself starts.
/// </summary>
public static class RunningHonestly
{
    /// <summary>
    /// Environment variables that load code into this process, or connect it to a tool, before it starts.
    ///
    /// Startup hooks run an initialiser before Main. The profiler pair loads a native DLL with full
    /// access to the runtime; <c>CORECLR_ENABLE_PROFILING</c> is the switch and
    /// <c>CORECLR_PROFILER_PATH</c> is what it loads (its <c>_64</c> and <c>_32</c> forms do nothing without
    /// the switch). Notification profilers load without that switch. A replacement garbage collector is a
    /// native library the runtime loads by name or path, and a diagnostic port connects the process to a
    /// tool as it starts; the runtime reads both under the <c>DOTNET_</c> and the older <c>COMPlus_</c> name.
    /// </summary>
    private static readonly string[] LoadOthersCode =
    [
        "DOTNET_STARTUP_HOOKS",
        "CORECLR_ENABLE_PROFILING",
        "CORECLR_PROFILER_PATH",
        "CORECLR_ENABLE_NOTIFICATION_PROFILERS",
        "CORECLR_NOTIFICATION_PROFILERS",
        "DOTNET_GCPath",
        "COMPlus_GCPath",
        "DOTNET_GCName",
        "COMPlus_GCName",
        "DOTNET_DiagnosticPorts",
        "COMPlus_DiagnosticPorts",
    ];

    /// <summary>The runtime's switch for its debugger, profiler and diagnostics pipe, read only from the environment.</summary>
    public const string DiagnosticsSwitch = "DOTNET_EnableDiagnostics";

    /// <summary>The prefixes of every name the .NET runtime reads from the environment before Main.</summary>
    private static readonly string[] RuntimePrefixes = ["CORECLR_", "DOTNET_", "COMPlus_"];

    /// <summary>
    /// Which of them are set, in the order they are checked. Empty is the ordinary answer.
    ///
    /// Names only. The value is a path somebody chose and has no business in a log line that a customer
    /// may read (INV-10).
    /// </summary>
    public static IReadOnlyList<string> ForeignCodeRequested(Func<string, string?>? read = null)
    {
        var lookup = read ?? Environment.GetEnvironmentVariable;
        return [.. LoadOthersCode.Where(name => !string.IsNullOrWhiteSpace(lookup(name)))];
    }

    /// <summary>
    /// Whether the refusal below may be a warning instead (B-02): only for a build that is not the
    /// installer's and carries no signature. The installer's build is compiled in, so it refuses whether or
    /// not it was signed; every build is unsigned until the certificate exists, and a rule keyed on the
    /// signature alone left the installed product warning on standard error, which nobody reads.
    /// </summary>
    /// <param name="installerBuild">This is the installer's build (each process's <c>BuildKind.Installer</c>).</param>
    /// <param name="carriesNoSignature">Nothing signed this process's image (<c>Signing.IsDevelopmentBuild</c>).</param>
    public static bool IsDevelopment(bool installerBuild, bool carriesNoSignature) => !installerBuild && carriesNoSignature;

    /// <summary>
    /// Makes <paramref name="environment"/> (a child's, before it starts) one in which the .NET runtime
    /// finds nothing to act on before Main: every <c>CORECLR_</c>, <c>DOTNET_</c> and <c>COMPlus_</c> name is
    /// removed, whatever its case, and <see cref="DiagnosticsSwitch"/> is set to 0, so the child loads no
    /// profiler or replacement collector, connects to no tool and opens no diagnostics pipe. Everything
    /// else is left as it is.
    /// </summary>
    public static void StartClean(IDictionary<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        foreach (var name in environment.Keys.Where(RuntimeSetting).ToList())
        {
            _ = environment.Remove(name);
        }

        environment[DiagnosticsSwitch] = "0";
    }

    private static bool RuntimeSetting(string name) =>
        RuntimePrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Why this process must not start, or null when it may.
    ///
    /// <paramref name="development"/> turns the refusal into a caller-visible warning: a build nobody
    /// ships should still be attachable to a profiler, and a rule that makes debugging impossible is a
    /// rule somebody deletes.
    /// </summary>
    public static string? WhyNotToStart(bool development, Func<string, string?>? read = null)
    {
        var requested = ForeignCodeRequested(read);
        if (requested.Count == 0 || development)
        {
            return null;
        }

        return $"{string.Join(" and ", requested)} asks this process to load code that is not ScreenTrail's, "
            + "so it will not start. Unset it and start ScreenTrail again.";
    }
}
