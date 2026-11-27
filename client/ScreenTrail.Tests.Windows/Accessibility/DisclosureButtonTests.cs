using System.Runtime.Versioning;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using CommunityToolkit.Mvvm.Input;
using ScreenTrail.Tests.Windows.Shell;
using ScreenTrail.UI.Controls;

namespace ScreenTrail.Tests.Windows.Accessibility;

/// <summary>
/// Review finding E-24: the timeline's toggle says whether the panel is open. Its peer has ExpandCollapse,
/// in step with IsExpanded, and Expand or Collapse runs the command only when the panel is the other way.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DisclosureButtonTests
{
    [Fact]
    public void TheButtonSaysCollapsedOrExpandedAndUiAutomationCanOpenAndCloseIt()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only.");
        StaThread.Run(() =>
        {
            var runs = 0;
            var button = new DisclosureButton();
            button.Command = new RelayCommand(() =>
            {
                runs++;
                button.IsExpanded = !button.IsExpanded;
            });

            var peer = UIElementAutomationPeer.CreatePeerForElement(button);
            var pattern = Assert.IsAssignableFrom<IExpandCollapseProvider>(peer.GetPattern(PatternInterface.ExpandCollapse));
            Assert.Equal(ExpandCollapseState.Collapsed, pattern.ExpandCollapseState);
            Assert.Equal(AutomationControlType.Button, peer.GetAutomationControlType());

            pattern.Expand();
            Assert.Equal(ExpandCollapseState.Expanded, pattern.ExpandCollapseState);
            pattern.Expand();
            Assert.Equal(1, runs);

            pattern.Collapse();
            Assert.Equal(ExpandCollapseState.Collapsed, pattern.ExpandCollapseState);
            Assert.Equal(2, runs);
            return Task.CompletedTask;
        });
    }
}
