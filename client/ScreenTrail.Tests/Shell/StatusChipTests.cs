using System.Xml.Linq;
using ScreenTrail.Core.Shell;
using ScreenTrail.Shared.Ipc;

namespace ScreenTrail.Tests.Shell;

/// <summary>The rail's status chip: one line, the time apart (review findings A-03, D-25, E-27).</summary>
public sealed class StatusChipTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Controls = "clr-namespace:ScreenTrail.UI.Controls";

    [Fact]
    public void TheWordsAndTheTimeAreSetApart()
    {
        var capture = new CaptureStateSnapshot { State = CaptureStates.Recording, SessionId = "s1", RemoteTool = "screenconnect", ElapsedMs = (12 * 60 + 27) * 1000 };
        var snapshot = new ShellSnapshot(ServiceConnection.Connected, capture, ShellView.Review, Banner: null);

        Assert.Equal("Recording · ScreenConnect", ShellStatus.ChipWords(snapshot));
        Assert.Equal("12:27", ShellStatus.ChipTime(snapshot));
        Assert.Equal("Recording · ScreenConnect · 12:27", ShellStatus.ChipLine(snapshot));
    }

    [Fact]
    public void AStateWithNoTimeShowsTheWordsAlone()
    {
        var snapshot = new ShellSnapshot(ServiceConnection.Connected, new CaptureStateSnapshot { State = CaptureStates.Idle }, ShellView.Review, Banner: null);

        Assert.Equal("Idle", ShellStatus.ChipWords(snapshot));
        Assert.Equal(string.Empty, ShellStatus.ChipTime(snapshot));
        Assert.Equal("Idle", ShellStatus.ChipLine(snapshot));
    }

    [Fact]
    public void TheChipIsOneLineThatTrimsItsWordsButNotItsTime()
    {
        var shell = Theme.EmptyStateTests.View("Shell", "ShellWindow.xaml");
        var chip = shell.Descendants(Wpf + "ContentControl").Single(c => (string?)c.Attribute("Style") == "{StaticResource Rail.StatusChip}");
        Assert.Equal("{Binding StatusLine}", (string?)chip.Attribute("ToolTip"));

        // No edge colour per state: border.strong in every tone.
        Assert.DoesNotContain(chip.Descendants(Wpf + "Setter"), s => (string?)s.Attribute("Property") == "BorderBrush");

        var words = chip.Descendants(Wpf + "TextBlock").Single();
        Assert.Equal("{Binding StatusWords}", (string?)words.Attribute("Text"));
        Assert.Equal("NoWrap", (string?)words.Attribute("TextWrapping"));
        Assert.Equal("CharacterEllipsis", (string?)words.Attribute("TextTrimming"));

        var time = chip.Descendants(Controls + "Glyph").Single();
        Assert.Equal("{Binding StatusTime}", (string?)time.Attribute("Text"));
        Assert.Equal("{StaticResource Text.Numeric}", (string?)time.Attribute("Style"));
        Assert.Null(time.Attribute("TextTrimming"));
    }
}
