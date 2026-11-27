using ScreenTrail.Core.Lifecycle;

namespace ScreenTrail.Tests.Lifecycle;

/// <summary>Which UI starts open the window, and the names the one running copy holds (review findings D-02, D-03).</summary>
public sealed class UiStartTests
{
    private const string Exe = @"C:\Program Files\ScreenTrail\ScreenTrail.UI.exe";

    [Fact]
    public void AStartFromStartOpensTheWindowAndASignInStartDoesNot()
    {
        Assert.True(UiStart.OpensWindow([]));
        Assert.False(UiStart.OpensWindow(["--background"]));
        Assert.False(UiStart.OpensWindow(["--BACKGROUND"]));
    }

    [Fact]
    public void TheSignInCommandStartsInTheBackground()
    {
        var command = UiStart.SignInCommand(Exe);

        Assert.Equal("\"" + Exe + "\" --background", command);
        Assert.Equal(Exe, ScreenTrail.Core.Install.InstalledFootprint.ExecutableOf(command));
    }

    [Fact]
    public void OnlyTheValueScreenTrailWroteBeforeTheFlagIsRewritten()
    {
        Assert.True(UiStart.IsSignInCommandWithoutTheFlag("\"" + Exe + "\"", Exe));
        Assert.False(UiStart.IsSignInCommandWithoutTheFlag(UiStart.SignInCommand(Exe), Exe));
        Assert.False(UiStart.IsSignInCommandWithoutTheFlag("\"" + Exe + "\" --minimised", Exe));
        Assert.False(UiStart.IsSignInCommandWithoutTheFlag("\"C:\\Elsewhere\\ScreenTrail.UI.exe\"", Exe));
        Assert.False(UiStart.IsSignInCommandWithoutTheFlag(null, Exe));
    }

    [Fact]
    public void TheNamesArePerSessionAndPerUser()
    {
        const string sid = "S-1-5-21-1-2-3-1001";

        Assert.Equal(@"Local\ScreenTrail.UI.S-1-5-21-1-2-3-1001", UiStart.InstanceName(sid));
        Assert.Equal(@"Local\ScreenTrail.UI.Show.S-1-5-21-1-2-3-1001", UiStart.ShowSignalName(sid));
        Assert.NotEqual(UiStart.InstanceName(sid), UiStart.InstanceName("S-1-5-21-1-2-3-1002"));
        _ = Assert.Throws<ArgumentException>(() => UiStart.InstanceName(@"Global\x"));
    }

    /// <summary>
    /// Review finding C-I7: the machine's sign-in start is in the background, and ends at once only for a
    /// technician who turned "Start UI at login" off; never chosen is on (owner decision 4). Restart
    /// Manager's start, and a start from Start, always go ahead.
    /// </summary>
    [Fact]
    public void TheMachineSignInStartStaysOutOnlyWhenTheTechnicianSaidSo()
    {
        var command = UiStart.MachineSignInCommand(Exe);
        Assert.Equal("\"" + Exe + "\" --background --at-sign-in", command);
        Assert.Equal(Exe, ScreenTrail.Core.Install.InstalledFootprint.ExecutableOf(command));
        Assert.False(UiStart.OpensWindow(["--background", "--at-sign-in"]));

        Assert.True(UiStart.StaysOutAtSignIn(["--background", "--at-sign-in"], false));
        Assert.False(UiStart.StaysOutAtSignIn(["--background", "--at-sign-in"], true));
        Assert.False(UiStart.StaysOutAtSignIn(["--background", "--at-sign-in"], null));
        Assert.False(UiStart.StaysOutAtSignIn(["--background"], false));
        Assert.False(UiStart.StaysOutAtSignIn([], false));
    }

    /// <summary>Review finding C-I7: the setting is on until someone turns it off.</summary>
    [Fact]
    public void StartUiAtLoginIsOnByDefault()
    {
        Assert.True(new ScreenTrail.Shared.Settings.CaptureSettings().StartUiAtLogin);
        Assert.True(System.Text.Json.JsonSerializer.Deserialize<ScreenTrail.Shared.Settings.CaptureSettings>("{}")!.StartUiAtLogin);
        Assert.False(System.Text.Json.JsonSerializer.Deserialize<ScreenTrail.Shared.Settings.CaptureSettings>("{\"start_ui_at_login\":false}")!.StartUiAtLogin);
        Assert.Null(new ScreenTrail.Core.Shell.ShellPreferences().StartAtSignIn);
    }
}
