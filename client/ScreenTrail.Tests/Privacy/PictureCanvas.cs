using ScreenTrail.Core.Privacy;
using ScreenTrail.Shared.Schema;

namespace ScreenTrail.Tests.Privacy;

/// <summary>
/// A frame drawn one brightness byte a pixel, for the checks the worker makes on the picture itself
/// (A-02): QR codes, and text the recogniser read no word of. Drawn rather than captured, so the same
/// pixels are checked on every machine and every pixel's origin is known.
/// </summary>
internal sealed class PictureCanvas
{
    /// <summary>
    /// A QR code's modules, version 2 (25 by 25), as a fixed bit matrix: the three finder patterns, their
    /// separators, the timing rows, the alignment pattern and fixed data bits. No library drew it, so the
    /// test needs none; what is checked is that the picture of a code is covered, not what it says.
    /// </summary>
    public static readonly string[] QrModules =
    [
        "#######..#...####.#######",
        "#.....#....######.#.....#",
        "#.###.#..##...#...#.###.#",
        "#.###.#..#####.##.#.###.#",
        "#.###.#.#.###.....#.###.#",
        "#.....#...###.#...#.....#",
        "#######.#.#.#.#.#.#######",
        ".........######.#........",
        "###...##..##.####.##..#..",
        "#..#.#..#..##.##..#.#####",
        "##..#.#.#..###.###.#.####",
        "##.......#........##..##.",
        "#...#.####.....#.....##..",
        "#..#...#...#.#.....###..#",
        "#######.##.##.#.##.###...",
        "....#..#..#...######....#",
        ".#.#..##.#..###.########.",
        "........#.###..##...##.##",
        "#######..##.##..#.#.###..",
        "#.....#.#.##....#...###..",
        "#.###.#.#.....#######...#",
        "#.###.#.#..#..##.##.#.#..",
        "#.###.#.....#.###.#.##.#.",
        "#.....#...##.#.####..####",
        "#######.#.#.##..#..##.#..",
    ];

    /// <summary>
    /// A 5 by 7 glyph for each character a drawn line uses: the shape of small screen text, one-pixel
    /// strokes, as a remote viewer shrinks a terminal or a password list.
    /// </summary>
    private static readonly Dictionary<char, string[]> Glyphs = new()
    {
        ['0'] = [".###.", "#...#", "#..##", "#.#.#", "##..#", "#...#", ".###."],
        ['1'] = ["..#..", ".##..", "..#..", "..#..", "..#..", "..#..", ".###."],
        ['2'] = [".###.", "#...#", "....#", "...#.", "..#..", ".#...", "#####"],
        ['3'] = ["####.", "....#", "....#", ".###.", "....#", "....#", "####."],
        ['4'] = ["...#.", "..##.", ".#.#.", "#..#.", "#####", "...#.", "...#."],
        ['5'] = ["#####", "#....", "####.", "....#", "....#", "#...#", ".###."],
        ['6'] = [".###.", "#....", "#....", "####.", "#...#", "#...#", ".###."],
        ['7'] = ["#####", "....#", "...#.", "..#..", ".#...", ".#...", ".#..."],
        ['8'] = [".###.", "#...#", "#...#", ".###.", "#...#", "#...#", ".###."],
        ['9'] = [".###.", "#...#", "#...#", ".####", "....#", "....#", ".###."],
        ['a'] = [".....", ".....", ".###.", "....#", ".####", "#...#", ".####"],
        ['b'] = ["#....", "#....", "####.", "#...#", "#...#", "#...#", "####."],
        ['c'] = [".....", ".....", ".###.", "#....", "#....", "#...#", ".###."],
        ['d'] = ["....#", "....#", ".####", "#...#", "#...#", "#...#", ".####"],
        ['e'] = [".....", ".....", ".###.", "#...#", "#####", "#....", ".###."],
        ['f'] = ["..##.", ".#..#", ".#...", "###..", ".#...", ".#...", ".#..."],
        ['k'] = ["#....", "#....", "#..#.", "#.#..", "##...", "#.#..", "#..#."],
        ['p'] = [".....", ".....", "####.", "#...#", "####.", "#....", "#...."],
        ['s'] = [".....", ".....", ".####", "#....", ".###.", "....#", "####."],
        ['w'] = [".....", ".....", "#...#", "#...#", "#.#.#", "#.#.#", ".#.#."],
        ['g'] = [".....", ".####", "#...#", "#...#", ".####", "....#", ".###."],
        ['h'] = ["#....", "#....", "####.", "#...#", "#...#", "#...#", "#...#"],
        ['i'] = ["..#..", ".....", ".##..", "..#..", "..#..", "..#..", ".###."],
        ['l'] = [".##..", "..#..", "..#..", "..#..", "..#..", "..#..", ".###."],
        ['m'] = [".....", ".....", "##.#.", "#.#.#", "#.#.#", "#.#.#", "#.#.#"],
        ['n'] = [".....", ".....", "####.", "#...#", "#...#", "#...#", "#...#"],
        ['o'] = [".....", ".....", ".###.", "#...#", "#...#", "#...#", ".###."],
        ['r'] = [".....", ".....", "#.##.", "##..#", "#....", "#....", "#...."],
        ['t'] = [".#...", ".#...", "####.", ".#...", ".#...", ".#..#", "..##."],
        ['u'] = [".....", ".....", "#...#", "#...#", "#...#", "#..##", ".##.#"],
        ['v'] = [".....", ".....", "#...#", "#...#", "#...#", ".#.#.", "..#.."],
        ['x'] = [".....", ".....", "#...#", ".#.#.", "..#..", ".#.#.", "#...#"],
        ['y'] = [".....", ".....", "#...#", "#...#", ".####", "....#", ".###."],
        ['_'] = [".....", ".....", ".....", ".....", ".....", ".....", "#####"],
        [':'] = [".....", "..#..", ".....", ".....", ".....", "..#..", "....."],
        ['='] = [".....", ".....", "#####", ".....", "#####", ".....", "....."],
        [' '] = [".....", ".....", ".....", ".....", ".....", ".....", "....."],
    };

    private readonly bool[] _ink;

    public PictureCanvas(int width, int height, byte paper = 255)
    {
        Width = width;
        Height = height;
        Pixels = Enumerable.Repeat(paper, width * height).ToArray();
        _ink = new bool[width * height];
    }

    public int Width { get; }

    public int Height { get; }

    public byte[] Pixels { get; }

    /// <summary>The frame as the worker is given it.</summary>
    public FrameBrightness Brightness() => new(Width, Height, [.. Pixels]);

    public void Fill(int x, int y, int width, int height, byte value, bool ink = false)
    {
        for (var row = Math.Max(0, y); row < Math.Min(Height, y + height); row++)
        {
            for (var column = Math.Max(0, x); column < Math.Min(Width, x + width); column++)
            {
                Pixels[(row * Width) + column] = value;
                _ink[(row * Width) + column] |= ink;
            }
        }
    }

    /// <summary>
    /// Draws <see cref="QrModules"/> with its top-left module at (<paramref name="x"/>, <paramref name="y"/>),
    /// with a quiet zone of four modules of paper round it. Returns the square the modules cover.
    /// </summary>
    public (int X, int Y, int Size) DrawQr(int x, int y, int module, byte dark = 0, byte light = 255)
    {
        var size = QrModules.Length * module;
        Fill(x - (4 * module), y - (4 * module), size + (8 * module), size + (8 * module), light);
        for (var row = 0; row < QrModules.Length; row++)
        {
            for (var column = 0; column < QrModules[row].Length; column++)
            {
                if (QrModules[row][column] == '#')
                {
                    Fill(x + (column * module), y + (row * module), module, module, dark, ink: true);
                }
            }
        }

        return (x, y, size);
    }

    /// <summary>
    /// Draws <paramref name="text"/> in the 5 by 7 glyphs, each pixel <paramref name="scale"/> pixels
    /// square, one blank column between glyphs. Returns the box the line covers.
    /// </summary>
    public (int X, int Y, int Width, int Height) DrawText(int x, int y, string text, int scale = 1, byte ink = 0)
    {
        ArgumentNullException.ThrowIfNull(text);
        var left = x;
        foreach (var character in text)
        {
            var glyph = Glyphs[character];
            for (var row = 0; row < glyph.Length; row++)
            {
                for (var column = 0; column < glyph[row].Length; column++)
                {
                    if (glyph[row][column] == '#')
                    {
                        Fill(left + (column * scale), y + (row * scale), scale, scale, ink, ink: true);
                    }
                }
            }

            left += 6 * scale;
        }

        return (x, y, left - x, 7 * scale);
    }

    /// <summary>
    /// A box blur of one pixel each way over the box given, as a viewer's resize and JPEG soften text:
    /// the strokes stay where they were drawn, their edges go grey.
    /// </summary>
    public void Soften(int x, int y, int width, int height)
    {
        var copy = (byte[])Pixels.Clone();
        for (var row = Math.Max(1, y); row < Math.Min(Height - 1, y + height); row++)
        {
            for (var column = Math.Max(1, x); column < Math.Min(Width - 1, x + width); column++)
            {
                var sum = 0;
                for (var dy = -1; dy <= 1; dy++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        sum += copy[((row + dy) * Width) + column + dx];
                    }
                }

                Pixels[(row * Width) + column] = (byte)(sum / 9);
            }
        }
    }

    /// <summary>How many of the drawn ink pixels inside the box given are not under any of the regions.</summary>
    public int InkLeft(IReadOnlyList<MaskedRegion> regions, int x, int y, int width, int height)
    {
        var left = 0;
        for (var row = Math.Max(0, y); row < Math.Min(Height, y + height); row++)
        {
            for (var column = Math.Max(0, x); column < Math.Min(Width, x + width); column++)
            {
                if (_ink[(row * Width) + column] && !regions.Any(r => column >= r.X && column < r.X + r.Width && row >= r.Y && row < r.Y + r.Height))
                {
                    left++;
                }
            }
        }

        return left;
    }

    /// <summary>How many drawn ink pixels the box given holds.</summary>
    public int Ink(int x, int y, int width, int height) => InkLeft([], x, y, width, height);
}
