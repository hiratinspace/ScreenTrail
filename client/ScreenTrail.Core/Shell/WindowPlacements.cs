using System.Collections;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using ScreenTrail.Core.Hud;

namespace ScreenTrail.Core.Shell;

/// <summary>
/// The shell window's last normal position and size, in device pixels, and whether it was maximised, for
/// one arrangement of monitors (review finding D-04). <paramref name="Layout"/> is <see cref="WindowPlacements.LayoutOf"/>.
/// </summary>
public sealed record WindowPlacement(int Left, int Top, int Right, int Bottom, bool Maximised, string Layout);

/// <summary>
/// The remembered placements, compared by what they hold, so <see cref="ShellPreferences"/> stays a record
/// whose equality means "the same preferences": a list there compared by reference, and a file read back
/// never equalled what was written.
/// </summary>
[CollectionBuilder(typeof(WindowPlacementList), nameof(Create))]
[JsonConverter(typeof(WindowPlacementListConverter))]
public sealed class WindowPlacementList : IReadOnlyList<WindowPlacement>, IEquatable<WindowPlacementList>
{
    private readonly WindowPlacement[] _items;

    private WindowPlacementList(WindowPlacement[] items) => _items = items;

    public static WindowPlacementList Empty { get; } = new([]);

    public static WindowPlacementList Create(ReadOnlySpan<WindowPlacement> items) => items.IsEmpty ? Empty : new(items.ToArray());

    public int Count => _items.Length;

    public WindowPlacement this[int index] => _items[index];

    public IEnumerator<WindowPlacement> GetEnumerator() => ((IEnumerable<WindowPlacement>)_items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public bool Equals(WindowPlacementList? other) => other is not null && _items.AsSpan().SequenceEqual(other._items);

    public override bool Equals(object? obj) => Equals(obj as WindowPlacementList);

    public override int GetHashCode()
    {
        var hash = default(HashCode);
        foreach (var item in _items)
        {
            hash.Add(item);
        }

        return hash.ToHashCode();
    }
}

/// <summary>Reads and writes the list as a plain JSON array; null or missing is empty.</summary>
public sealed class WindowPlacementListConverter : JsonConverter<WindowPlacementList>
{
    public override WindowPlacementList Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        JsonSerializer.Deserialize<WindowPlacement[]>(ref reader, options) is { } items ? WindowPlacementList.Create(items.Where(i => i is not null).ToArray()) : WindowPlacementList.Empty;

    public override void Write(Utf8JsonWriter writer, WindowPlacementList value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);
        JsonSerializer.Serialize(writer, value.ToArray(), options);
    }
}

/// <summary>
/// Where the shell window opens (review finding D-04, design system §11: "window position and size persist
/// per monitor layout"). It opened at 1280 by 800 wherever Windows put it, every time, since each open
/// builds a new window. Now it opens where it was closed on the same monitors: a laptop docked to two
/// screens and the same laptop on its own each keep their own place. A place that is no longer at least
/// half on a work area is not used, and the window opens in the middle of the monitor under the pointer.
/// </summary>
public static class WindowPlacements
{
    /// <summary>How many monitor arrangements are remembered; the least recently used goes first.</summary>
    public const int Kept = 4;

    /// <summary>The arrangement as text: every monitor's rectangle, in order, so the same monitors in the same places match.</summary>
    public static string LayoutOf(IReadOnlyList<ScreenArea> monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        return string.Join(';', monitors
            .OrderBy(m => m.Left).ThenBy(m => m.Top)
            .Select(m => string.Create(CultureInfo.InvariantCulture, $"{m.Left},{m.Top},{m.Right},{m.Bottom}")));
    }

    /// <summary>The placement to open at on these monitors, or null to centre on the monitor under the pointer.</summary>
    public static WindowPlacement? ToRestore(IEnumerable<WindowPlacement>? saved, IReadOnlyList<ScreenArea> monitors, IReadOnlyList<ScreenArea> workAreas)
    {
        ArgumentNullException.ThrowIfNull(workAreas);
        var layout = LayoutOf(monitors);
        var match = saved?.FirstOrDefault(p => p.Layout == layout);
        if (match is null || match.Right <= match.Left || match.Bottom <= match.Top)
        {
            return null;
        }

        double area = (double)(match.Right - match.Left) * (match.Bottom - match.Top);
        var shown = workAreas.Sum(w =>
            Math.Max(0, Math.Min(match.Right, w.Right) - Math.Max(match.Left, w.Left))
            * Math.Max(0, Math.Min(match.Bottom, w.Bottom) - Math.Max(match.Top, w.Top)));
        return shown >= area / 2 ? match : null;
    }

    /// <summary>The remembered placements with <paramref name="now"/> first, in place of any for the same monitors.</summary>
    public static WindowPlacementList With(IEnumerable<WindowPlacement>? saved, WindowPlacement now)
    {
        ArgumentNullException.ThrowIfNull(now);
        return [now, .. (saved ?? []).Where(p => p.Layout != now.Layout).Take(Kept - 1)];
    }
}
