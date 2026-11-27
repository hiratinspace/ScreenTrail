using ScreenTrail.Shared.Ipc;

namespace ScreenTrail.Core.Shell;

/// <summary>The mark on the shell's taskbar button (review finding D-15).</summary>
public enum TaskbarMark
{
    /// <summary>Not recording: no mark.</summary>
    None,

    /// <summary>Recording: the 8 px dot in state.recording.</summary>
    Recording,

    /// <summary>Paused, by hand or by the privacy pause: the pause mark in state.paused.</summary>
    Paused,
}

/// <summary>
/// Review finding D-15: with the shell minimised during a session the taskbar button gave no hint that
/// capture was on (the pill and the tray do, so INV-4 held). Premium Windows apps put live state there as
/// an overlay. The words beside it are the shell's status announcement.
/// </summary>
public static class TaskbarOverlay
{
    public static TaskbarMark For(ShellSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Connection != ServiceConnection.Connected)
        {
            return TaskbarMark.None;
        }

        return snapshot.Capture?.State switch
        {
            CaptureStates.Recording => TaskbarMark.Recording,
            CaptureStates.Paused or CaptureStates.Suppressed => TaskbarMark.Paused,
            _ => TaskbarMark.None,
        };
    }
}
