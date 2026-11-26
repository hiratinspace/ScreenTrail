using System.Windows;
using System.Windows.Controls;

namespace ScreenTrail.UI.Theme;

/// <summary>
/// When a tooltip shows and for how long (design system 10.6, review finding B-28): 600 ms after the pointer
/// rests, 100 ms between one tip and the next, ten seconds on screen, and on keyboard focus as well as
/// hover. Set once for every element, before the first window.
/// </summary>
internal static class ToolTipTiming
{
    public const int InitialShowDelay = 600;

    public const int BetweenShowDelay = 100;

    public const int ShowDuration = 10000;

    private static bool _applied;

    public static void Apply()
    {
        if (_applied)
        {
            return;
        }

        _applied = true;
        ToolTipService.InitialShowDelayProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(InitialShowDelay));
        ToolTipService.BetweenShowDelayProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(BetweenShowDelay));
        ToolTipService.ShowDurationProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(ShowDuration));
        ToolTipService.ShowsToolTipOnKeyboardFocusProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(true));
    }
}
