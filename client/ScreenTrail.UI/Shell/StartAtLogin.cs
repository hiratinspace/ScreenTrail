using System.IO;
using Microsoft.Win32;
using ScreenTrail.Core.Install;
using ScreenTrail.Core.Lifecycle;
using ScreenTrail.Core.Shell;

namespace ScreenTrail.UI.Shell;

/// <summary>
/// Spec §5 S5 "Start UI at login": the UI process in the technician's Run key (ST-080). The UI rather
/// than the service, because the service is a Windows service and this is the tray icon and the pill.
/// Nothing here is content; a failure to write is reported, not thrown, because a settings save that
/// took should not look like one that did not.
/// </summary>
internal static class StartAtLogin
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private const string ValueName = "ScreenTrail";

    /// <summary>The value's name before the product was renamed (ScreenTail, until 2026-11-17).</summary>
    private const string LegacyValueName = "ScreenTail";

    /// <summary>
    /// A technician who had "Start UI at login" on before the rename has a value under the old name that
    /// starts a program the upgrade removed. It is replaced by today's value pointing at this program, so
    /// the setting stays on; with no old value there is nothing to do. Called once at the window's start.
    ///
    /// A value written before the sign-in start said <see cref="UiStart.BackgroundFlag"/> gains it, or the
    /// UI would open its window at every sign-in now that a plain start opens one (review finding D-03).
    /// </summary>
    public static void MigrateLegacyValue()
    {
        try
        {
            using var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (run is null)
            {
                return;
            }

            // Review finding C-I7: installed, the machine's Run value starts the UI, so the technician's own
            // would start it a second time at every sign-in (the second copy exits, but it is a wasted start).
            if (MachineValueExists())
            {
                run.DeleteValue(ValueName, throwOnMissingValue: false);
                run.DeleteValue(LegacyValueName, throwOnMissingValue: false);
            }
            else if (run.GetValue(LegacyValueName) is string)
            {
                run.SetValue(ValueName, UiStart.SignInCommand(Environment.ProcessPath!));
                run.DeleteValue(LegacyValueName, throwOnMissingValue: false);
            }
            else if (UiStart.IsSignInCommandWithoutTheFlag(run.GetValue(ValueName) as string, Environment.ProcessPath!))
            {
                run.SetValue(ValueName, UiStart.SignInCommand(Environment.ProcessPath!));
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            // Left as it was: the setting can still be turned on again in Settings.
        }
    }

    /// <summary>
    /// Saves "Start UI at login". Installed, the machine's Run value starts the UI for everyone (review
    /// finding C-I7), so the choice is kept in the shell's preferences, where that start reads it, and the
    /// technician's own Run value is not written. A copy run without the installer (a developer's) has no
    /// machine value and uses the technician's Run value as before.
    /// </summary>
    public static bool Apply(bool enabled, ShellPreferencesStore preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        var kept = preferences.Save(preferences.Load() with { StartAtSignIn = enabled });
        try
        {
            using var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true) ?? Registry.CurrentUser.CreateSubKey(RunKey);
            if (MachineValueExists())
            {
                run.DeleteValue(ValueName, throwOnMissingValue: false);
                return kept;
            }

            if (enabled)
            {
                run.SetValue(ValueName, UiStart.SignInCommand(Environment.ProcessPath!));
            }
            else
            {
                run.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return false;
        }
    }

    /// <summary>Whether the installer's machine Run value for the UI is there.</summary>
    private static bool MachineValueExists()
    {
        try
        {
            using var machine = Registry.LocalMachine.OpenSubKey(InstalledFootprint.RunKey);
            return machine?.GetValue(InstalledFootprint.UiRunValue) is string;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return false;
        }
    }
}
