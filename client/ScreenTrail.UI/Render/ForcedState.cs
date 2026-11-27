using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using ScreenTrail.UI.Gallery;

namespace ScreenTrail.UI.Render;

/// <summary>
/// Draws a control hovered, pressed or focused without a pointer or a keyboard (review finding B-56). A
/// render cannot set IsMouseOver or IsKeyboardFocused, so this runs the control's own triggers for them:
/// every trigger in its template and its style chain that is on a forced property, or has a condition on
/// one, and whose other conditions hold now, has its setters set on the control and its named parts. A
/// template's triggers go first and the style's after, the order WPF ranks them in, so what is drawn is
/// the style the product ships, not a copy of it. For the gallery only: it sets local values.
/// </summary>
internal static class ForcedState
{
    public static void Apply(FrameworkElement element, GalleryState state)
    {
        DependencyProperty[] forced = state switch
        {
            GalleryState.Hover => [UIElement.IsMouseOverProperty],
            GalleryState.Pressed => [UIElement.IsMouseOverProperty, ButtonBase.IsPressedProperty, Theme.Pressed.IsPressedProperty],
            GalleryState.Focused => [UIElement.IsKeyboardFocusedProperty, UIElement.IsKeyboardFocusWithinProperty, UIElement.IsFocusedProperty],
            _ => [],
        };
        if (forced.Length == 0)
        {
            return;
        }

        _ = element.ApplyTemplate();
        if (element is Control { Template: { } template } control)
        {
            Run(template.Triggers, forced, element, name => template.FindName(name, control) as DependencyObject);
        }

        var styles = new List<Style>();
        for (var style = element.Style; style is not null; style = style.BasedOn)
        {
            styles.Insert(0, style);
        }

        foreach (var style in styles)
        {
            Run(style.Triggers, forced, element, _ => null);
        }
    }

    private static void Run(TriggerCollection triggers, DependencyProperty[] forced, FrameworkElement element, Func<string, DependencyObject?> named)
    {
        DependencyObject? Source(string? name) => string.IsNullOrEmpty(name) ? element : named(name);
        bool Holds(string? source, DependencyProperty property, object? value) =>
            Source(source) is { } on && (Array.IndexOf(forced, property) >= 0 ? Equals(value, true) : Equals(on.GetValue(property), value));

        foreach (var trigger in triggers)
        {
            var setters = trigger switch
            {
                Trigger one when Array.IndexOf(forced, one.Property) >= 0 && Holds(one.SourceName, one.Property, one.Value) => one.Setters,
                MultiTrigger many when many.Conditions.Any(c => Array.IndexOf(forced, c.Property) >= 0)
                    && many.Conditions.All(c => Holds(c.SourceName, c.Property, c.Value)) => many.Setters,
                _ => null,
            };
            if (setters is null)
            {
                continue;
            }

            foreach (var setter in setters.OfType<Setter>())
            {
                Set(string.IsNullOrEmpty(setter.TargetName) ? element : named(setter.TargetName), setter.Property, setter.Value);
            }
        }
    }

    private static void Set(DependencyObject? target, DependencyProperty property, object? value)
    {
        switch (target, value)
        {
            case (null, _):
                return;
            case (FrameworkElement framework, DynamicResourceExtension resource):
                framework.SetResourceReference(property, resource.ResourceKey);
                return;
            case (FrameworkContentElement content, DynamicResourceExtension resource):
                content.SetResourceReference(property, resource.ResourceKey);
                return;
            case (_, BindingBase binding):
                _ = BindingOperations.SetBinding(target, property, binding);
                return;
            case (_, System.Windows.Markup.MarkupExtension):
                // A template binding or another extension WPF resolves itself: not one a local value can say.
                return;
            default:
                target.SetValue(property, value);
                return;
        }
    }
}
