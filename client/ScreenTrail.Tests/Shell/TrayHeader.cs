using ScreenTrail.Core.Shell;

namespace ScreenTrail.Tests.Shell;

/// <summary>
/// The tray menu's header as the tests write it, "Not recording · 2 drafts ready · 3 publishes not
/// confirmed": the state on its first line, what waits on the second, comma separated (review finding C-T5).
/// </summary>
internal static class TrayHeader
{
    public static void Is(string written, TrayPresence presence)
    {
        var parts = written.Split(" · ");
        Assert.Equal(parts[0], presence.StateLine);
        Assert.Equal(parts.Length > 1 ? string.Join(", ", parts[1..]) : null, presence.Counts);
    }
}
