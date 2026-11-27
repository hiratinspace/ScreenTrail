using System.Windows;
using System.Windows.Media;
using ScreenTrail.Core.Shell;

namespace ScreenTrail.UI.Shell;

/// <summary>
/// The picture on the shell's taskbar button (review finding D-15, design system 11 "Taskbar and Alt+Tab"):
/// a frozen 16 px drawing, the 8 px dot in state.recording while recording, two bars in state.paused while
/// paused, and none otherwise. Drawn from the current theme's brushes, so a theme change redraws it.
/// </summary>
internal static class TaskbarMarkImage
{
    private const double Box = 16;

    public static ImageSource? For(TaskbarMark mark) => mark switch
    {
        TaskbarMark.Recording => Draw(new GeometryDrawing(Brush("Brush.state.recording"), null, new EllipseGeometry(new Point(Box / 2, Box / 2), 4, 4))),
        TaskbarMark.Paused => Draw(new GeometryDrawing(Brush("Brush.state.paused"), null, new GeometryGroup
        {
            Children =
            {
                new RectangleGeometry(new Rect(4, 3, 3, 10), 1, 1),
                new RectangleGeometry(new Rect(9, 3, 3, 10), 1, 1),
            },
        })),
        _ => null,
    };

    private static DrawingImage Draw(GeometryDrawing mark)
    {
        // The box is drawn clear, so the mark sits centred in 16 px whatever its own bounds.
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, Box, Box))));
        group.Children.Add(mark);
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }

    private static Brush Brush(string key) =>
        Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
}
