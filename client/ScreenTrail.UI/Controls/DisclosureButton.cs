using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;

namespace ScreenTrail.UI.Controls;

/// <summary>
/// A button that opens and closes a panel, and says which it is (review finding E-24). A plain Button
/// left the state to a "▸" or "▾" in its words, which a screen reader cannot read as open or closed; a
/// ToggleButton would say "on" or "off". This one's peer has the ExpandCollapse pattern, so Narrator says
/// "collapsed" or "expanded", and Expand or Collapse from UI Automation runs the button's command when the
/// panel is not already that way.
/// </summary>
public sealed class DisclosureButton : Button
{
    public static readonly DependencyProperty IsExpandedProperty = DependencyProperty.Register(
        nameof(IsExpanded), typeof(bool), typeof(DisclosureButton), new FrameworkPropertyMetadata(false, OnIsExpandedChanged));

    public bool IsExpanded
    {
        get => (bool)GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    private static void OnIsExpandedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (UIElementAutomationPeer.FromElement((DisclosureButton)d) is Peer peer)
        {
            peer.RaisePropertyChangedEvent(
                ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty,
                State((bool)e.OldValue),
                State((bool)e.NewValue));
        }
    }

    private static ExpandCollapseState State(bool expanded) => expanded ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

    private sealed class Peer(DisclosureButton owner) : ButtonAutomationPeer(owner), IExpandCollapseProvider
    {
        public ExpandCollapseState ExpandCollapseState => State(owner.IsExpanded);

        public override object GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.ExpandCollapse ? this : base.GetPattern(patternInterface);

        public void Expand() => Toggle(to: true);

        public void Collapse() => Toggle(to: false);

        private void Toggle(bool to)
        {
            if (!owner.IsEnabled)
            {
                throw new ElementNotEnabledException();
            }

            if (owner.IsExpanded != to && owner.Command is { } command && command.CanExecute(owner.CommandParameter))
            {
                command.Execute(owner.CommandParameter);
            }
        }
    }
}
