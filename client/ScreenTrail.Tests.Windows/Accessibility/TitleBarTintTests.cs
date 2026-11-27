using System.Runtime.Versioning;
using ScreenTrail.Core.Shell;
using ScreenTrail.UI.Shell;
using ScreenTrail.UI.Theme;
using AppTheme = ScreenTrail.UI.Theme.AppTheme;

namespace ScreenTrail.Tests.Windows.Accessibility;

/// <summary>
/// Spec v0.6.0 §3 Frame (built v0.6.12): every window's title bar is tinted with the frame's colours,
/// follows the theme when it changes, and is the system's own in high contrast. DWM draws the title bar
/// outside WPF, so no render shows it; this holds what the shell asks DWM for, on a real window handle.
/// </summary>
[SupportedOSPlatform("windows")]
[Collection(WpfApplicationOwner.Name)]
public sealed class TitleBarTintTests(TheApplication app)
{
    [Fact]
    public async Task TheShellsTitleBarIsTheFrameInEveryTheme()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only.");
        var real = ThemeManager.SystemHighContrast;
        try
        {
            await app.RunAsync(async () =>
            {
                var shell = await Screens.OpenAsync("review");
                try
                {
                    Assert.True(TitleBar.GetTint(shell), "The shell is not built on Window.Base's title bar tint.");

                    ThemeManager.Apply(AppTheme.Dark, app.Resources);
                    await Audit.Settle();
                    Assert.Equal(TitleBarTint.For(false, "#0D111B", "#F7F5FE", "#4A546B"), TitleBar.GetApplied(shell));

                    ThemeManager.Apply(AppTheme.Light, app.Resources);
                    await Audit.Settle();
                    Assert.Equal(TitleBarTint.For(false, "#ECEDF1", "#121826", "#BFC4CF"), TitleBar.GetApplied(shell));

                    ThemeManager.SystemHighContrast = () => true;
                    ThemeManager.Apply(AppTheme.Light, app.Resources);
                    await Audit.Settle();
                    Assert.Equal(TitleBarTint.System, TitleBar.GetApplied(shell));
                }
                finally
                {
                    await Screens.CloseAsync(shell);
                }
            });
        }
        finally
        {
            ThemeManager.SystemHighContrast = real;
            await app.RunAsync(() => ThemeManager.Apply(AppTheme.Dark, app.Resources));
        }
    }

    [Fact]
    public async Task TheOneDialogTakesTheTintToo()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only.");
        await app.RunAsync(async () =>
        {
            var dialog = await Screens.ShowModalAsync(new ConfirmWindow(ConfirmCopy.QuitWhileCapturing));
            try
            {
                Assert.True(TitleBar.GetTint(dialog));

                // Review finding D-12: the dialog's caption is its surface (dark bg.surface under
                // text.primary), and its edge border.strong, so it is one sheet with an edge.
                Assert.Equal(TitleBarCaption.Surface, TitleBar.GetCaption(dialog));
                Assert.Equal(TitleBarTint.For(false, "#1D2536", "#F1F2F6", "#4A546B"), TitleBar.GetApplied(dialog));
            }
            finally
            {
                await Screens.CloseAsync(dialog);
            }
        });
    }

    [Fact]
    public async Task TheWindowIsPaintedThePageColourBeforeWpfDrawsIt()
    {
        // Review finding D-13: the window's own background was Windows' white, which showed for a frame on
        // every dark-mode open. It is the theme's page colour, and follows the theme.
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only.");
        await app.RunAsync(async () =>
        {
            var shell = await Screens.OpenAsync("review");
            try
            {
                var target = System.Windows.Interop.HwndSource.FromVisual(shell) is System.Windows.Interop.HwndSource source ? source.CompositionTarget : null;
                Assert.NotNull(target);

                ThemeManager.Apply(AppTheme.Dark, app.Resources);
                await Audit.Settle();
                Assert.Equal(System.Windows.Media.Color.FromRgb(0x12, 0x18, 0x26), target.BackgroundColor);

                ThemeManager.Apply(AppTheme.Light, app.Resources);
                await Audit.Settle();
                Assert.Equal(System.Windows.Media.Color.FromRgb(0xF8, 0xF8, 0xFA), target.BackgroundColor);
            }
            finally
            {
                await Screens.CloseAsync(shell);
                ThemeManager.Apply(AppTheme.Dark, app.Resources);
            }
        });
    }
}
