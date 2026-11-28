using ScreenTrail.Shared.Schema;

namespace ScreenTrail.Core.Privacy;

/// <summary>
/// Finds the parts of a frame that look like text where the recogniser read no word, so they are masked
/// in the picture rather than kept readable (A-02 (a)).
///
/// The recogniser reads part of a frame as often as all of it: small or scaled text in a remote viewer,
/// low contrast, a script its language does not cover. Masking can only cover what was read, so a frame
/// read in part used to be stored with the rest of its text showing. Here the frame is cut into cells of
/// <see cref="Cell"/> pixels and each is judged on the pixels no word box (and no mask already found)
/// covers: a cell looks like text when several of its rows cross many sharp edges, it has edges up and
/// down as well as across, much of it is one flat shade (the paper), and a good share of its edges have
/// paper on one side (ink drawn on paper, where a photograph's edges are between its own shades). One cell alone is not enough (an
/// icon, a cursor); cells that look like text beside one another are, and each such run is grown along its
/// row by up to four cells, and then by one up and down, into neighbours with any edge in them, so the
/// ends of a line and the tops and tails of its letters are not left. The cells are joined
/// into rectangles, one for each block of text, and those are masked.
///
/// The frame is kept and the cells masked, rather than the frame discarded: what was read stays readable,
/// and a screenshot with blocks painted over is worth more to the technician than none.
///
/// What it does not cover: text in a cell alone, text whose strokes are softer than <see cref="Edge"/>
/// brightness steps, and text drawn over a busy picture (a photograph), where no shade is the paper.
/// </summary>
internal static class TextCoverage
{
    /// <summary>The side of a cell, in pixels.</summary>
    internal const int Cell = 16;

    /// <summary>A step in brightness between neighbouring pixels at least this large is an edge.</summary>
    internal const int Edge = 12;

    /// <summary>A row of a cell with at least this many edges across is busy.</summary>
    private const int EdgesInARow = 3;

    /// <summary>A cell with at least this many busy rows, and this many edges up and down, has text's edges.</summary>
    private const int BusyRows = 3;

    private const int EdgesDown = 3;

    /// <summary>How many cells a block of text is grown by along its rows, into neighbours with any edge in them.</summary>
    private const int Growth = 4;

    /// <summary>
    /// At least this share of a cell within sixteen brightness levels (two of 32 shades side by side) of
    /// one another: it has paper. Text's paper is one flat shade; a photograph's texture, softened or not,
    /// spreads wider.
    /// </summary>
    private const double Paper = 0.35;

    /// <summary>Brightness in 32 shades of eight levels each, for the paper test.</summary>
    private const int Shades = 32;

    /// <summary>At least this share of a text-like cell's edges have paper on one side: ink drawn on paper.</summary>
    private const double PaperEdges = 0.25;

    /// <summary>
    /// Masks past this many on one frame are one: the box round them all. A frame of scattered text is
    /// masked more, never less.
    /// </summary>
    private const int MaxRegions = 128;

    /// <summary>
    /// The rectangles to mask: the text-like parts of the frame that no word in <paramref name="words"/>
    /// and no region in <paramref name="masked"/> covers.
    /// </summary>
    public static IReadOnlyList<MaskedRegion> Unread(FrameBrightness frame, IReadOnlyList<OcrWord> words, IReadOnlyList<MaskedRegion> masked)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(words);
        ArgumentNullException.ThrowIfNull(masked);
        var (width, height, pixels) = (frame.Width, frame.Height, frame.Pixels);
        var read = Covered(width, height, words, masked);
        var (across, down) = ((width + Cell - 1) / Cell, (height + Cell - 1) / Cell);
        var cells = across * down;
        // First each cell's paper: the two side-by-side shades most of it is.
        var shades = new int[cells * Shades];
        for (var y = 0; y < height; y++)
        {
            var row = pixels.AsSpan(y * width, width);
            var cellRow = (y / Cell) * across;
            for (var x = 0; x < width; x++)
            {
                shades[((cellRow + (x / Cell)) * Shades) + (row[x] >> 3)]++;
            }
        }

        var paper = new int[cells];
        var flat = new bool[cells];
        for (var cell = 0; cell < cells; cell++)
        {
            var (cx, cy) = (cell % across, cell / across);
            var size = (Math.Min(width, (cx + 1) * Cell) - (cx * Cell)) * (Math.Min(height, (cy + 1) * Cell) - (cy * Cell));
            var most = 0;
            for (var shade = 0; shade + 1 < Shades; shade++)
            {
                var near = shades[(cell * Shades) + shade] + shades[(cell * Shades) + shade + 1];
                if (near > most)
                {
                    (most, paper[cell]) = (near, shade);
                }
            }

            flat[cell] = most >= Paper * size;
        }

        // Then the edges no word covers: across in each row, up and down, and how many have paper on one side.
        var busy = new int[cells];
        var vertical = new int[cells];
        var edges = new int[cells];
        var onPaper = new int[cells];
        var ink = new bool[cells];
        var inRow = new int[across];
        for (var y = 0; y < height; y++)
        {
            var row = y * width;
            var cellRow = (y / Cell) * across;
            Array.Clear(inRow);
            for (var x = 0; x < width; x++)
            {
                var i = row + x;
                if (read[i])
                {
                    continue;
                }

                var cell = cellRow + (x / Cell);
                var value = pixels[i];
                var shade = paper[cell];
                if (x + 1 < width && !read[i + 1] && Math.Abs(pixels[i + 1] - value) >= Edge)
                {
                    inRow[x / Cell]++;
                    ink[cell] = true;
                    edges[cell]++;
                    onPaper[cell] += IsPaper(value, shade) || IsPaper(pixels[i + 1], shade) ? 1 : 0;
                }

                if (y + 1 < height && !read[i + width] && Math.Abs(pixels[i + width] - value) >= Edge)
                {
                    vertical[cell]++;
                    ink[cell] = true;
                    edges[cell]++;
                    onPaper[cell] += IsPaper(value, shade) || IsPaper(pixels[i + width], shade) ? 1 : 0;
                }
            }

            for (var cx = 0; cx < across; cx++)
            {
                busy[cellRow + cx] += inRow[cx] >= EdgesInARow ? 1 : 0;
            }
        }

        // Text-like: text's edges, on paper. A photograph's edges are mostly between two of its own shades.
        var textLike = new bool[cells];
        for (var cell = 0; cell < cells; cell++)
        {
            textLike[cell] = flat[cell] && busy[cell] >= BusyRows && vertical[cell] >= EdgesDown && onPaper[cell] >= PaperEdges * edges[cell];
        }

        // Not alone, then grown by a cell into neighbours with any edge in them.
        var kept = new bool[cells];
        for (var cell = 0; cell < cells; cell++)
        {
            kept[cell] = textLike[cell] && Neighbours(cell, across, down).Any(n => textLike[n]);
        }

        // Along the row first, where a line's ends are, then a cell up and down, where its tops and tails are.
        var grown = kept;
        for (var step = 0; step <= Growth; step++)
        {
            var from = grown;
            grown = (bool[])from.Clone();
            for (var cell = 0; cell < cells; cell++)
            {
                var beside = step < Growth
                    ? (cell % across > 0 && from[cell - 1]) || (cell % across + 1 < across && from[cell + 1])
                    : Neighbours(cell, across, down).Any(n => from[n]);
                if (!from[cell] && ink[cell] && beside)
                {
                    grown[cell] = true;
                }
            }
        }

        return Rectangles(grown, across, down, width, height);
    }

    private static bool IsPaper(byte value, int shade) => (value >> 3) - shade is 0 or 1;

    /// <summary>The pixels a word or a mask already covers: a word with a margin of a quarter of its height.</summary>
    private static bool[] Covered(int width, int height, IReadOnlyList<OcrWord> words, IReadOnlyList<MaskedRegion> masked)
    {
        var covered = new bool[width * height];
        void Fill(long left, long top, long right, long bottom)
        {
            var (x0, x1) = ((int)Math.Clamp(left, 0, width), (int)Math.Clamp(right, 0, width));
            for (var y = (int)Math.Clamp(top, 0, height); y < Math.Clamp(bottom, 0, height) && x1 > x0; y++)
            {
                Array.Fill(covered, true, (y * width) + x0, x1 - x0);
            }
        }

        foreach (var word in words)
        {
            var margin = Math.Max(2, word.Height / 4);
            Fill(word.X - margin, word.Y - margin, (long)word.X + word.Width + margin, (long)word.Y + word.Height + margin);
        }

        foreach (var region in masked)
        {
            Fill(region.X, region.Y, region.X + region.Width, region.Y + region.Height);
        }

        return covered;
    }

    private static IEnumerable<int> Neighbours(int cell, int across, int down)
    {
        var (cx, cy) = (cell % across, cell / across);
        if (cx > 0)
        {
            yield return cell - 1;
        }

        if (cx + 1 < across)
        {
            yield return cell + 1;
        }

        if (cy > 0)
        {
            yield return cell - across;
        }

        if (cy + 1 < down)
        {
            yield return cell + across;
        }
    }

    /// <summary>
    /// The cells as rectangles: each row's runs, a run joined to the block above it where they share most
    /// of their width, and blocks that then overlap joined, so a block of text is one mask.
    /// </summary>
    private static List<MaskedRegion> Rectangles(bool[] cells, int across, int down, int width, int height)
    {
        // In cells: left and right (exclusive), top and bottom (exclusive).
        var done = new List<(int Left, int Right, int Top, int Bottom)>();
        var open = new List<(int Left, int Right, int Top, int Bottom)>();
        for (var cy = 0; cy < down; cy++)
        {
            var next = new List<(int Left, int Right, int Top, int Bottom)>();
            for (var cx = 0; cx < across; cx++)
            {
                if (!cells[(cy * across) + cx])
                {
                    continue;
                }

                var start = cx;
                while (cx < across && cells[(cy * across) + cx])
                {
                    cx++;
                }

                var run = (Left: start, Right: cx);
                var joined = next.FindIndex(r => r.Left <= run.Right && run.Left <= r.Right);
                if (joined >= 0)
                {
                    var r = next[joined];
                    next[joined] = (Math.Min(r.Left, run.Left), Math.Max(r.Right, run.Right), r.Top, r.Bottom);
                    continue;
                }

                var above = open.FindIndex(r => Math.Min(r.Right, run.Right) - Math.Max(r.Left, run.Left) >= 0.5 * Math.Min(r.Right - r.Left, run.Right - run.Left));
                if (above >= 0)
                {
                    var r = open[above];
                    open.RemoveAt(above);
                    next.Add((Math.Min(r.Left, run.Left), Math.Max(r.Right, run.Right), r.Top, cy + 1));
                }
                else
                {
                    next.Add((run.Left, run.Right, cy, cy + 1));
                }
            }

            done.AddRange(open);
            open = next;
        }

        done.AddRange(open);

        // Blocks that overlap after growing are one block. A frame scattered with more blocks than are
        // worth joining one by one is masked by the box round them all.
        if (done.Count > 4 * MaxRegions)
        {
            done = [(done.Min(r => r.Left), done.Max(r => r.Right), done.Min(r => r.Top), done.Max(r => r.Bottom))];
        }

        for (var merged = true; merged;)
        {
            merged = false;
            for (var i = 0; i < done.Count && !merged; i++)
            {
                for (var j = i + 1; j < done.Count && !merged; j++)
                {
                    var (a, b) = (done[i], done[j]);
                    if (a.Left < b.Right && b.Left < a.Right && a.Top < b.Bottom && b.Top < a.Bottom)
                    {
                        done[i] = (Math.Min(a.Left, b.Left), Math.Max(a.Right, b.Right), Math.Min(a.Top, b.Top), Math.Max(a.Bottom, b.Bottom));
                        done.RemoveAt(j);
                        merged = true;
                    }
                }
            }
        }

        if (done.Count > MaxRegions)
        {
            done = [(done.Min(r => r.Left), done.Max(r => r.Right), done.Min(r => r.Top), done.Max(r => r.Bottom))];
        }

        return [.. done.Select(r => new MaskedRegion
        {
            X = r.Left * Cell,
            Y = r.Top * Cell,
            Width = Math.Min(width, r.Right * Cell) - (r.Left * Cell),
            Height = Math.Min(height, r.Bottom * Cell) - (r.Top * Cell),
            Kind = MaskKind.SensitiveContext,
        })];
    }
}
