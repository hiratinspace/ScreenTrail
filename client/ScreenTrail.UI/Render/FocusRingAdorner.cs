using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace ScreenTrail.UI.Render;

/// <summary>
/// What WPF's own focus visual is: a Control styled with the element's FocusVisualStyle, sized to the
/// element. The renders draw it themselves, since WPF decides "focus arrived by keyboard" from input a
/// harness cannot really send. Shared by the shell's focus renders and the gallery's Focused column.
/// </summary>
internal sealed class FocusRingAdorner : Adorner
{
    private readonly Control _ring;

    public FocusRingAdorner(UIElement adorned, Style style)
        : base(adorned)
    {
        _ring = new Control { Style = style, Focusable = false };
        IsHitTestVisible = false;
        AddVisualChild(_ring);
    }

    protected override int VisualChildrenCount => 1;

    protected override Visual GetVisualChild(int index) => _ring;

    protected override Size MeasureOverride(Size constraint)
    {
        _ring.Measure(AdornedElement.RenderSize);
        return AdornedElement.RenderSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _ring.Arrange(new Rect(finalSize));
        return finalSize;
    }
}
