using System.Runtime.Versioning;
using System.Windows;
using ScreenTrail.Tests.Windows.Accessibility;
using ScreenTrail.UI.Shell;

namespace ScreenTrail.Tests.Windows.Shell;

/// <summary>
/// Open in the tray, Open last draft and the pill's double-click bring a minimised window back, the way it
/// was before it was minimised (review finding D-05). Show and Activate alone left it on the taskbar.
/// </summary>
[SupportedOSPlatform("windows")]
[Collection(WpfApplicationOwner.Name)]
public sealed class WindowRestoreTests(TheApplication app)
{
    [Theory]
    [InlineData(WindowState.Normal)]
    [InlineData(WindowState.Maximized)]
    public async Task AMinimisedWindowComesBackAsItWas(WindowState before)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only.");
        await app.RunAsync(() =>
        {
            var window = new Window { Width = 400, Height = 300, ShowActivated = false };
            var restore = new WindowRestore(window);
            try
            {
                window.Show();
                window.WindowState = before;
                window.WindowState = WindowState.Minimized;

                restore.Bring(window);

                Assert.Equal(before, window.WindowState);
            }
            finally
            {
                window.Close();
            }

            return Task.CompletedTask;
        });
    }
}
