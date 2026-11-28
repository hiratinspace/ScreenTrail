namespace ScreenTrail.Core.Net;

/// <summary>Where the backend address in force came from, for the service's start-up log. Never the address itself.</summary>
public enum AddressOrigin
{
    /// <summary>None was configured: nothing is sent, and sessions are drafted on the device.</summary>
    None,

    /// <summary>The machine-wide setting an administrator wrote (the installer's <c>BACKENDURL</c>).</summary>
    Machine,

    /// <summary><c>SCREENTRAIL_BACKEND</c>: development builds, the demo and the end-to-end run only.</summary>
    Environment,
}

/// <param name="Backend">The address, or null when none was configured.</param>
/// <param name="Origin">Where it came from.</param>
/// <param name="EnvironmentIgnored">An installed build found the environment variables set and did not use them.</param>
/// <param name="AdministratorNote">
/// What the technician is told when an installed build has no machine-wide address: only an administrator
/// can set it, so the device card, an activation and diagnostics say so rather than a bare "no backend".
/// Null otherwise.
/// </param>
public sealed record DeploymentAddress(Uri? Backend, AddressOrigin Origin, bool EnvironmentIgnored, string? AdministratorNote = null);

/// <summary>
/// Where the capture service learns its backend and, in development, its device token (2026-11-15 review).
///
/// The address decides where the egress allowlist points and where the tenant's policy is fetched from,
/// so a locked local-only is only as strong as the place the address is read from. A per-user
/// environment variable is a setting the technician, or anything running as them, can change; an
/// installed build reads the address only from the machine-wide setting, which only an administrator
/// can write, and ignores the variables. Development builds (not the installer's build, unsigned, and not
/// running from the install folder) keep the variables, because the test loop, the demo and the end-to-end
/// run are built on them, with the machine setting as their fallback: but only on a machine with no
/// machine-wide address, or whose administrator allowed development overrides
/// (<see cref="DevelopmentOverridesValue"/>). Where an administrator set the address, a build that does not
/// count as installed reads it too, so a copy of the release that has lost what makes it count (renamed,
/// moved, its signature stripped) still cannot be pointed elsewhere.
///
/// The same rule holds for <c>SCREENTRAIL_DEVICE_TOKEN</c>: an installed build is enrolled with an
/// invite's code (ST-010) and never with a token from its environment.
///
/// In Core, so every platform's suite runs it; the service reads the registry and the signature.
/// </summary>
public static class DeploymentAddresses
{
    /// <summary>The backend address for development builds.</summary>
    public const string BackendVariable = "SCREENTRAIL_BACKEND";

    /// <summary>The device token for development builds (the M1 runbook's, and the demo's).</summary>
    public const string DeviceTokenVariable = "SCREENTRAIL_DEVICE_TOKEN";

    /// <summary>The machine-wide key the installer writes, under HKEY_LOCAL_MACHINE (64-bit view).</summary>
    public const string MachineKey = @"SOFTWARE\ScreenTrail";

    /// <summary>
    /// The key before the product was renamed (ScreenTail, until 2026-11-17). Read only when
    /// <see cref="MachineKey"/> has no address, for a machine whose MSP tooling wrote the old key itself:
    /// the installer carries an address from the old key to the new one on upgrade.
    /// </summary>
    public const string LegacyMachineKey = @"SOFTWARE\ScreenTail";

    /// <summary>The value under <see cref="MachineKey"/> holding the backend address.</summary>
    public const string MachineValue = "BackendUrl";

    /// <summary>An installed build with no machine-wide address: the technician cannot fix it, an administrator can.</summary>
    public const string NoMachineAddress =
        "This computer has no backend address set, so sessions are drafted here and nothing can be published. Ask your ScreenTrail admin to set it (BACKENDURL when installing ScreenTrail).";

    /// <summary>
    /// The value under <see cref="MachineKey"/> an administrator sets to 1 on a development machine that also
    /// has <see cref="MachineValue"/>, so a development build there still reads the variables. Absent on a
    /// customer's machine. Never read by an installed build.
    /// </summary>
    public const string DevelopmentOverridesValue = "AllowDevelopmentOverrides";

    /// <summary>Added when the same build also found SCREENTRAIL_BACKEND set, which it does not read.</summary>
    public const string EnvironmentNotRead = " An installed copy does not read SCREENTRAIL_BACKEND.";

    /// <param name="installed">An installed build (<see cref="Installed"/>).</param>
    /// <param name="machine">The machine-wide setting, or null when there is none.</param>
    /// <param name="environment">Reads an environment variable by name.</param>
    /// <param name="developmentOverridesAllowed">The administrator's <see cref="DevelopmentOverridesValue"/> (<see cref="DevelopmentOverridesAllowed"/>).</param>
    public static DeploymentAddress Resolve(bool installed, string? machine, Func<string, string?> environment, bool developmentOverridesAllowed = false)
    {
        ArgumentNullException.ThrowIfNull(environment);
        var fromMachine = Parse(machine);
        if (!ReadsEnvironment(installed, machine, developmentOverridesAllowed))
        {
            var set = !string.IsNullOrEmpty(environment(BackendVariable)) || !string.IsNullOrEmpty(environment(DeviceTokenVariable));
            if (fromMachine is null)
            {
                var note = string.IsNullOrEmpty(environment(BackendVariable)) ? NoMachineAddress : NoMachineAddress + EnvironmentNotRead;
                return new DeploymentAddress(null, AddressOrigin.None, set, note);
            }

            return new DeploymentAddress(fromMachine, AddressOrigin.Machine, set);
        }

        if (Parse(environment(BackendVariable)) is { } fromEnvironment)
        {
            return new DeploymentAddress(fromEnvironment, AddressOrigin.Environment, false);
        }

        return new DeploymentAddress(fromMachine, fromMachine is null ? AddressOrigin.None : AddressOrigin.Machine, false);
    }

    /// <summary>
    /// The development fallback token, or null for an installed build, which never takes one from its
    /// environment, and for any build on a machine with an address unless development overrides are allowed.
    /// </summary>
    /// <param name="machine">The machine-wide address, as <see cref="Resolve"/> takes it.</param>
    /// <param name="developmentOverridesAllowed">As <see cref="Resolve"/> takes it.</param>
    public static Func<string?>? FallbackToken(bool installed, Func<string, string?> environment, string? machine = null, bool developmentOverridesAllowed = false)
    {
        ArgumentNullException.ThrowIfNull(environment);
        return ReadsEnvironment(installed, machine, developmentOverridesAllowed) ? () => environment(DeviceTokenVariable) : null;
    }

    /// <summary>
    /// Whether the variables are read at all: never by an installed build, and by any other only where the
    /// machine has no address (an administrator never set one) or its administrator allowed development
    /// overrides. A machine address that is present counts whether or not it parses.
    /// </summary>
    public static bool ReadsEnvironment(bool installed, string? machine, bool developmentOverridesAllowed) =>
        !installed && (machine is null || developmentOverridesAllowed);

    /// <summary>
    /// Whether the registry value <see cref="DevelopmentOverridesValue"/> allows overrides: a DWORD other
    /// than 0, or the text 1 or true. Anything else, or nothing, does not.
    /// </summary>
    public static bool DevelopmentOverridesAllowed(object? value) => value switch
    {
        int number => number != 0,
        string text => text.Trim() is "1" || string.Equals(text.Trim(), "true", StringComparison.OrdinalIgnoreCase),
        _ => false,
    };

    /// <summary>The folder the installer puts ScreenTrail in, under the machine's 64-bit Program Files.</summary>
    public const string InstallFolderName = "ScreenTrail";

    /// <summary>The service's own executable (its apphost), as the package ships it.</summary>
    public const string ServiceExecutableName = "ScreenTrail.Service.exe";

    /// <summary>
    /// Whether the running service is an installed build, which reads its address only from the machine.
    ///
    /// It is when it is the build the installer packages (<paramref name="installerBuild"/>, compiled in by
    /// <c>ScreenTrailInstaller=true</c>), wherever that build runs, whatever its file is called and whether
    /// or not it is signed: that is a fact every copy of the release carries with it, where a folder, a file
    /// name and a signature are each something a copy can shed. It is also when it runs from the install
    /// folder (<see cref="InstalledAt"/>), and when it is the signed service executable started from
    /// anywhere else. A development build is none of these and keeps the variables (but see
    /// <see cref="ReadsEnvironment"/>), and <c>dotnet ScreenTrail.Service.dll</c> is never installed by its
    /// signature, because the process is the .NET host.
    ///
    /// The signature rule counts any valid signature, and only for the service's own file name. Being
    /// installed only ever takes something away (the variables, the development token, the technician's
    /// switches over the built-in redaction rules), so a signer this does not know cannot gain by it.
    /// </summary>
    /// <param name="processPath">The running process's image (<c>Environment.ProcessPath</c>).</param>
    /// <param name="programFiles">As <see cref="InstalledAt"/> takes it.</param>
    /// <param name="carriesSignature">Whether that image carries a valid Authenticode signature. Asked only for the service's own executable.</param>
    /// <param name="installerBuild">This is the installer's build (the service's <c>BuildKind.Installer</c>).</param>
    public static bool Installed(string? processPath, string? programFiles, Func<string, bool> carriesSignature, bool installerBuild = false)
    {
        ArgumentNullException.ThrowIfNull(carriesSignature);
        if (installerBuild || InstalledAt(processPath, programFiles))
        {
            return true;
        }

        return !string.IsNullOrEmpty(processPath)
            && string.Equals(Path.GetFileName(processPath), ServiceExecutableName, StringComparison.OrdinalIgnoreCase)
            && carriesSignature(processPath);
    }

    /// <summary>
    /// True when <paramref name="executable"/> sits inside <c>ScreenTrail</c> under
    /// <paramref name="programFiles"/>: the one folder the package installs into (its RefuseInstallFolder
    /// step refuses any other) and one only an administrator can write. Decided by location: a signature
    /// on its own would pass any signer, the .NET host under Program Files included, so
    /// <see cref="Installed"/> counts one only for the service's own executable.
    /// </summary>
    /// <param name="programFiles">
    /// The machine's Program Files, from the known folder (<c>Environment.SpecialFolder.ProgramFiles</c> in
    /// a 64-bit process), never from <c>%ProgramW6432%</c>: a per-user variable can override that one.
    /// </param>
    public static bool InstalledAt(string? executable, string? programFiles)
    {
        if (string.IsNullOrEmpty(executable) || string.IsNullOrEmpty(programFiles))
        {
            return false;
        }

        var full = Path.GetFullPath(executable);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(programFiles, InstallFolderName))) + Path.DirectorySeparatorChar;
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static Uri? Parse(string? value) =>
        Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var url) ? url : null;
}
