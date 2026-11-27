namespace ScreenTrail.Core.Input;

/// <summary>
/// The chords as they are bound now, for every string that teaches one (review finding E-03). The pill,
/// the tray, the empty screens and the service's refusals each spelled out the shipped defaults, so after
/// a technician rebound Pause in Settings the app kept teaching the old chord. One source, set where the
/// bindings are known: the service as it registers them (HotkeyHost), the UI as it reads or saves its
/// capture settings (PipeCapture). Until then the shipped defaults, which is what the service binds when
/// nothing was changed.
/// </summary>
public static class HotkeyText
{
    private static IReadOnlyDictionary<HotkeyAction, Hotkey> _bound = HotkeyBindings.Defaults;

    /// <summary>Raised when the bindings change, so a surface showing a chord can say it again.</summary>
    public static event EventHandler? Changed;

    /// <summary>The bindings to speak of from now on.</summary>
    public static void Use(HotkeyBindings bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        var next = new Dictionary<HotkeyAction, Hotkey>(bindings.All);
        var same = next.Count == _bound.Count && next.All(b => _bound.TryGetValue(b.Key, out var was) && was == b.Value);
        _bound = next;
        if (!same)
        {
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }

    /// <summary>"Ctrl+Alt+P", or empty for an action with no chord (Discard, unless a tenant binds it).</summary>
    public static string For(HotkeyAction action) => _bound.TryGetValue(action, out var hotkey) ? hotkey.ToString() : string.Empty;

    /// <summary>
    /// The chord held on one line with word joiners (U+2060) between its parts, for prose that wraps: a
    /// wrapped "Ctrl+Alt+R" broke after "Ctrl".
    /// </summary>
    public static string Unbroken(HotkeyAction action) => For(action).Replace("+", "⁠+⁠", StringComparison.Ordinal);
}
