using System.Runtime.Versioning;
using System.Security.Principal;
using ScreenTrail.Service.Install;

namespace ScreenTrail.Tests.Windows.Install;

/// <summary>
/// C-07: the uninstall step, which removes every user's key and credentials, and the upgrade step run only
/// for the system account, which is how Windows Installer runs them. An elevated administrator's prompt is
/// not the installer: as one, the session list is empty, so every user would look signed out and have
/// their key removed. The hosted runner is an elevated administrator, so this runs there.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class InstallerCallerTests
{
    [Fact]
    public void OnlyTheSystemAccountIsTheInstaller()
    {
        using var me = WindowsIdentity.GetCurrent();
        var administrator = new WindowsPrincipal(me).IsInRole(WindowsBuiltInRole.Administrator);

        Assert.Equal(me.IsSystem, UninstallCleanup.RunByTheInstaller());
        if (administrator && !me.IsSystem)
        {
            Assert.False(UninstallCleanup.RunByTheInstaller(), "an elevated administrator who is not the system account is not the installer");
        }
    }
}
