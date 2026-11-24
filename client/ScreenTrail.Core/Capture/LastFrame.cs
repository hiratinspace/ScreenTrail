namespace ScreenTrail.Core.Capture;

/// <summary>
/// What the last frame kept in this session looked like, shared by the click and scene paths (ST-025, ST-026).
///
/// The owner, 2026-11-23, after one-minute laptop sessions kept 15 to 19 frames: "the tool captures a lot
/// of screenshots which is unnecessary, it should only capture the relevant things with steps." Two causes:
///
/// - A click got a frame whenever 400 ms had passed since the last one, whatever the screen showed, so
///   clicking around a screen that did not change photographed it again each time.
/// - The two paths never compared notes. A click that opened a dialog got a frame, and the scene sampler,
///   still comparing against its own last frame, then photographed the same dialog again.
///
/// Now a click frame is skipped when the window looks practically the same as the last frame kept
/// (<see cref="Repeats"/>: within <see cref="RepeatWithin"/> bits of the 256-bit hash, tighter than the
/// sampler's own threshold of 10 so a ticked box or a highlighted row still counts as a change), and the
/// sampler compares against whatever frame was kept last, click or scene. The click itself is still
/// recorded on the timeline; only the duplicate picture is left out. Marked moments are never skipped.
/// </summary>
public sealed class LastFrame(int repeatWithin = LastFrame.DefaultRepeatWithin)
{
    /// <summary>Half the sampler's medium threshold: a change the sampler would ignore can still matter to a click.</summary>
    public const int DefaultRepeatWithin = 5;

    private readonly Lock _gate = new();
    private SceneHash? _hash;
    private long _version;
    private long _repeats;

    public int RepeatWithin { get; } = Math.Max(0, repeatWithin);

    /// <summary>Click frames left out because the window looked like the last frame kept. A count, for diagnostics.</summary>
    public long Repeated => Interlocked.Read(ref _repeats);

    /// <summary>Whether a picture with this hash would repeat the last frame kept, counting it if so.</summary>
    public bool Repeats(SceneHash hash)
    {
        lock (_gate)
        {
            if (_hash is { } last && last.DistanceTo(hash) <= RepeatWithin)
            {
                _repeats++;
                return true;
            }

            return false;
        }
    }

    /// <summary>A frame with this hash was kept.</summary>
    public void Kept(SceneHash hash)
    {
        lock (_gate)
        {
            _hash = hash;
            _version++;
        }
    }

    /// <summary>
    /// The last frame kept, if one was kept since <paramref name="seen"/>; moves <paramref name="seen"/> on.
    /// How the scene sampler learns of a click frame, so it does not photograph the same screen again.
    /// </summary>
    public bool TryNewer(ref long seen, out SceneHash hash)
    {
        lock (_gate)
        {
            if (_version != seen && _hash is { } last)
            {
                seen = _version;
                hash = last;
                return true;
            }

            hash = default;
            return false;
        }
    }

    /// <summary>A new session: nothing kept yet, so its first click is photographed.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _hash = null;
            _version++;
        }
    }
}
