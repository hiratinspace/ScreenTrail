using ScreenTrail.Shared.Schema;

namespace ScreenTrail.Core.Privacy;

/// <summary>
/// Finds QR codes in a frame, so the picture of one is masked (A-02 (c)).
///
/// A QR code is never text, so no pattern can find it, and the code is often the secret itself: an MFA
/// enrolment code carries the key that makes the second factor, a Wi-Fi code the network's password. A
/// code is found by its three finder patterns, the nested squares in three of its corners: a dark ring,
/// a light ring and a dark centre, one, one, three, one and one modules across in every direction. Where
/// three of them stand at the corners of a square, that square and a margin round it are masked.
///
/// The checks are on the frame's brightness, made dark and light against the frame's own contrast near
/// each point, both ways round (a dark code on light, and a light one on dark). Each candidate is
/// checked across, down, across again at its centre, and at the corners of its rings: a ring's corners
/// are dark and a circle's are not, so a selected radio button is not taken for one.
///
/// What it does not find: a code turned on its side by other than a right angle, a code drawn so small
/// a module is under a pixel, and a code with a finder pattern covered or cut off by the frame's edge.
/// </summary>
internal static class QrCodeFinder
{
    /// <summary>The side of the square a pixel's dark or light is judged against: 3 by 3 of these.</summary>
    private const int Tile = 32;

    /// <summary>Below this much contrast near a point, the area is one shade and has no edges to read.</summary>
    private const int MinimumContrast = 48;

    /// <summary>Candidates kept from one frame, so a frame of noise cannot make the search grow without end.</summary>
    private const int MaxCandidates = 256;

    /// <summary>Finder patterns paired into codes, at most: the strongest first.</summary>
    private const int MaxFinders = 48;

    /// <summary>How far past a finder's centre, in modules, the code's square and its margin reach.</summary>
    private const double Reach = 4.5;

    /// <summary>The squares of every QR code found, in frame pixels.</summary>
    public static IReadOnlyList<MaskedRegion> Find(FrameBrightness frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var dark = Binarise(frame);
        var found = new List<MaskedRegion>();
        foreach (var ink in new[] { true, false })
        {
            var finders = Finders(dark, frame.Width, frame.Height, ink);
            found.AddRange(Codes(finders, frame.Width, frame.Height));
        }

        return found;
    }

    /// <summary>Each pixel dark (true) or light, against the contrast of the 3 by 3 tiles round it.</summary>
    private static bool[] Binarise(FrameBrightness frame)
    {
        var (width, height, pixels) = (frame.Width, frame.Height, frame.Pixels);
        var (across, down) = ((width + Tile - 1) / Tile, (height + Tile - 1) / Tile);
        var low = new byte[across * down];
        var high = new byte[across * down];
        Array.Fill(low, byte.MaxValue);
        for (var y = 0; y < height; y++)
        {
            var row = pixels.AsSpan(y * width, width);
            var tileRow = (y / Tile) * across;
            for (var x = 0; x < width; x++)
            {
                var tile = tileRow + (x / Tile);
                var value = row[x];
                if (value < low[tile])
                {
                    low[tile] = value;
                }

                if (value > high[tile])
                {
                    high[tile] = value;
                }
            }
        }

        // A pixel is dark below its threshold: the middle of the lowest and highest near it, or, where
        // the area is one shade, all dark or all light by that shade.
        var threshold = new int[across * down];
        for (var ty = 0; ty < down; ty++)
        {
            for (var tx = 0; tx < across; tx++)
            {
                int lo = byte.MaxValue, hi = 0;
                for (var ny = Math.Max(0, ty - 1); ny <= Math.Min(down - 1, ty + 1); ny++)
                {
                    for (var nx = Math.Max(0, tx - 1); nx <= Math.Min(across - 1, tx + 1); nx++)
                    {
                        lo = Math.Min(lo, low[(ny * across) + nx]);
                        hi = Math.Max(hi, high[(ny * across) + nx]);
                    }
                }

                threshold[(ty * across) + tx] = hi - lo >= MinimumContrast
                    ? (lo + hi + 1) / 2
                    : (lo + hi) / 2 >= 128 ? 0 : 256;
            }
        }

        var dark = new bool[width * height];
        for (var y = 0; y < height; y++)
        {
            var row = pixels.AsSpan(y * width, width);
            var tileRow = (y / Tile) * across;
            for (var x = 0; x < width; x++)
            {
                dark[(y * width) + x] = row[x] < threshold[tileRow + (x / Tile)];
            }
        }

        return dark;
    }

    private readonly record struct Finder(double X, double Y, double Module, int Hits);

    /// <summary>
    /// The five runs across one finder pattern from a point in its centre, along one axis: ring, light,
    /// centre, light, ring. Positions are offsets from the point, in pixels, to the middle of each run.
    /// </summary>
    private readonly record struct Runs(int Ring0, int Light0, int Centre, int Light1, int Ring1, double Middle, double RingBefore, double LightBefore, double LightAfter, double RingAfter)
    {
        public int Total => Ring0 + Light0 + Centre + Light1 + Ring1;

        public bool InRatio => Ratio(Ring0, Light0, Centre, Light1, Ring1);
    }

    /// <summary>One, one, three, one, one, within half a module each (three halves for the centre).</summary>
    private static bool Ratio(int a, int b, int c, int d, int e)
    {
        var total = a + b + c + d + e;
        if (total < 7)
        {
            return false;
        }

        var module = total / 7.0;
        var slack = (module / 2) + 0.5;
        return Math.Abs(a - module) < slack && Math.Abs(b - module) < slack && Math.Abs(c - (3 * module)) < 3 * slack
            && Math.Abs(d - module) < slack && Math.Abs(e - module) < slack;
    }

    /// <summary>
    /// Walks from (<paramref name="x"/>, <paramref name="y"/>), which is ink, both ways along one axis,
    /// counting the centre, the light ring and the dark ring on each side.
    /// </summary>
    private static bool Walk(bool[] dark, int width, int height, bool ink, int x, int y, int dx, int dy, int limit, out Runs runs)
    {
        runs = default;
        if (x < 0 || y < 0 || x >= width || y >= height || dark[(y * width) + x] != ink)
        {
            return false;
        }

        Span<int> before = stackalloc int[3];
        Span<int> after = stackalloc int[3];
        for (var side = 0; side < 2; side++)
        {
            var counts = side == 0 ? before : after;
            var step = side == 0 ? -1 : 1;
            var (px, py) = side == 0 ? (x, y) : (x + dx, y + dy);
            for (var run = 0; run < 3; run++)
            {
                var want = run != 1 ? ink : !ink;
                while (px >= 0 && py >= 0 && px < width && py < height && dark[(py * width) + px] == want && counts[run] <= limit)
                {
                    counts[run]++;
                    px += step * dx;
                    py += step * dy;
                }

                if (counts[run] > limit || (run > 0 && counts[run] == 0))
                {
                    return false;
                }
            }
        }

        // Offsets of each run's first and last pixel from the starting one: the centre from -(b0 - 1)
        // to a1, then outward.
        var (a0, b0, c0, a1, b1, c1) = (before[0], before[1], before[2], after[0], after[1], after[2]);
        double Mid(int first, int last) => (first + last) / 2.0;
        runs = new Runs(
            c0, b0, a0 + a1, b1, c1,
            Middle: Mid(-(a0 - 1), a1),
            RingBefore: Mid(-(a0 - 1) - b0 - c0, -(a0 - 1) - b0 - 1),
            LightBefore: Mid(-(a0 - 1) - b0, -a0),
            LightAfter: Mid(a1 + 1, a1 + b1),
            RingAfter: Mid(a1 + b1 + 1, a1 + b1 + c1));
        return true;
    }

    private static bool Ink(bool[] dark, int width, int height, bool ink, double x, double y)
    {
        var (px, py) = ((int)Math.Round(x), (int)Math.Round(y));
        return px >= 0 && py >= 0 && px < width && py < height && dark[(py * width) + px] == ink;
    }

    /// <summary>Every finder pattern of the colour given, seen on at least two rows.</summary>
    private static List<Finder> Finders(bool[] dark, int width, int height, bool ink)
    {
        var candidates = new List<Finder>();
        var starts = new int[width + 1];
        for (var y = 0; y < height && candidates.Count < MaxCandidates; y++)
        {
            // The row as runs: where each starts, alternating colour from the first pixel's.
            var row = y * width;
            var count = 0;
            starts[count++] = 0;
            for (var x = 1; x < width; x++)
            {
                if (dark[row + x] != dark[row + x - 1])
                {
                    starts[count++] = x;
                }
            }

            starts[count] = width;
            var first = dark[row] == ink ? 0 : 1;
            for (var i = first; i + 5 <= count; i += 2)
            {
                int Length(int run) => starts[run + 1] - starts[run];
                if (!Ratio(Length(i), Length(i + 1), Length(i + 2), Length(i + 3), Length(i + 4)))
                {
                    continue;
                }

                var across = starts[i + 5] - starts[i];
                var centreX = starts[i + 2] + (Length(i + 2) / 2);
                if (Confirm(dark, width, height, ink, centreX, y, across) is { } found)
                {
                    Merge(candidates, found);
                    if (candidates.Count >= MaxCandidates)
                    {
                        break;
                    }
                }
            }
        }

        return [.. candidates.Where(c => c.Hits >= 2).OrderByDescending(c => c.Hits).Take(MaxFinders)];
    }

    /// <summary>
    /// A row's one, one, three, one, one checked down through its centre, across again at the centre
    /// that gives, down again there, and at the corners of both rings.
    /// </summary>
    private static Finder? Confirm(bool[] dark, int width, int height, bool ink, int x, int y, int across)
    {
        if (!Walk(dark, width, height, ink, x, y, 0, 1, across, out var down) || !down.InRatio || 5 * Math.Abs(down.Total - across) >= 2 * across)
        {
            return null;
        }

        var cy = (int)Math.Round(y + down.Middle);
        if (!Walk(dark, width, height, ink, x, cy, 1, 0, across * 2, out var horizontal) || !horizontal.InRatio)
        {
            return null;
        }

        var cx = (int)Math.Round(x + horizontal.Middle);
        if (!Walk(dark, width, height, ink, cx, cy, 0, 1, across * 2, out var vertical) || !vertical.InRatio
            || 5 * Math.Abs(vertical.Total - horizontal.Total) >= 2 * Math.Max(vertical.Total, horizontal.Total))
        {
            return null;
        }

        // The rings are squares: their corners are the ring's colour. A circle's are not.
        foreach (var rx in new[] { x + horizontal.RingBefore, x + horizontal.RingAfter })
        {
            foreach (var ry in new[] { cy + vertical.RingBefore, cy + vertical.RingAfter })
            {
                if (!Ink(dark, width, height, ink, rx, ry))
                {
                    return null;
                }
            }
        }

        foreach (var lx in new[] { x + horizontal.LightBefore, x + horizontal.LightAfter })
        {
            foreach (var ly in new[] { cy + vertical.LightBefore, cy + vertical.LightAfter })
            {
                if (!Ink(dark, width, height, !ink, lx, ly))
                {
                    return null;
                }
            }
        }

        return new Finder(x + horizontal.Middle, cy + vertical.Middle, (horizontal.Total + vertical.Total) / 14.0, 1);
    }

    /// <summary>The same finder seen from another row is one finder seen twice.</summary>
    private static void Merge(List<Finder> candidates, Finder found)
    {
        for (var i = 0; i < candidates.Count; i++)
        {
            var known = candidates[i];
            var near = Math.Max(known.Module, found.Module) * 2;
            if (Math.Abs(known.X - found.X) <= near && Math.Abs(known.Y - found.Y) <= near
                && Math.Max(known.Module, found.Module) <= 1.5 * Math.Min(known.Module, found.Module))
            {
                var hits = known.Hits + 1;
                candidates[i] = new Finder(
                    ((known.X * known.Hits) + found.X) / hits,
                    ((known.Y * known.Hits) + found.Y) / hits,
                    ((known.Module * known.Hits) + found.Module) / hits,
                    hits);
                return;
            }
        }

        candidates.Add(found);
    }

    /// <summary>
    /// Three finders at the corners of a square, of one module size, are a code: its square, from the
    /// three centres and the fourth corner they imply, out to its margin.
    /// </summary>
    private static IEnumerable<MaskedRegion> Codes(List<Finder> finders, int width, int height)
    {
        for (var i = 0; i < finders.Count; i++)
        {
            for (var j = i + 1; j < finders.Count; j++)
            {
                for (var k = j + 1; k < finders.Count; k++)
                {
                    if (Square(finders[i], finders[j], finders[k], width, height) is { } code)
                    {
                        yield return code;
                    }
                }
            }
        }
    }

    private static MaskedRegion? Square(Finder a, Finder b, Finder c, int width, int height)
    {
        var modules = new[] { a.Module, b.Module, c.Module };
        if (modules.Max() > 1.5 * modules.Min())
        {
            return null;
        }

        // The corner is the finder opposite the longest side.
        static double Distance(Finder p, Finder q) => Math.Sqrt(((p.X - q.X) * (p.X - q.X)) + ((p.Y - q.Y) * (p.Y - q.Y)));
        var (ab, ac, bc) = (Distance(a, b), Distance(a, c), Distance(b, c));
        var (corner, one, two) = bc >= ab && bc >= ac ? (a, b, c) : ac >= ab ? (b, a, c) : (c, a, b);
        var (leg1, leg2) = (Distance(corner, one), Distance(corner, two));
        var module = modules.Average();
        var cosine = (((one.X - corner.X) * (two.X - corner.X)) + ((one.Y - corner.Y) * (two.Y - corner.Y))) / (leg1 * leg2);
        if (Math.Abs(leg1 - leg2) > 0.15 * Math.Max(leg1, leg2) || Math.Abs(cosine) > 0.15
            || Math.Min(leg1, leg2) / module < 12 || Math.Max(leg1, leg2) / module > 175)
        {
            return null;
        }

        var (fourthX, fourthY) = (one.X + two.X - corner.X, one.Y + two.Y - corner.Y);
        var reach = (Reach * module) + 1;
        var left = Math.Max(0, (int)Math.Floor(new[] { corner.X, one.X, two.X, fourthX }.Min() - reach));
        var top = Math.Max(0, (int)Math.Floor(new[] { corner.Y, one.Y, two.Y, fourthY }.Min() - reach));
        var right = Math.Min(width, (int)Math.Ceiling(new[] { corner.X, one.X, two.X, fourthX }.Max() + reach + 1));
        var bottom = Math.Min(height, (int)Math.Ceiling(new[] { corner.Y, one.Y, two.Y, fourthY }.Max() + reach + 1));
        return right <= left || bottom <= top
            ? null
            : new MaskedRegion { X = left, Y = top, Width = right - left, Height = bottom - top, Kind = MaskKind.SensitiveContext };
    }
}
