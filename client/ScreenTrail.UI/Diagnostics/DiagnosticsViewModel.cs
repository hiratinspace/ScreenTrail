using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoreShell = ScreenTrail.Core.Shell;

namespace ScreenTrail.UI.Diagnostics;

/// <param name="Label">The left column. Fixed wording, never derived from anything captured.</param>
/// <param name="Kind">What the value is (review finding B-54): words, a count in tabular figures, or an identifier in mono.</param>
public sealed record DiagnosticsRow(string Label, string Value, CoreShell.DiagnosticsValue Kind = CoreShell.DiagnosticsValue.Words);

/// <summary>
/// The "What's being captured right now?" panel (ST-071, Spec §5 S1).
///
/// Every row comes from <see cref="CoreShell.DiagnosticsPanel"/> over <see cref="CoreShell.Diagnostics"/>,
/// which has nowhere to put content, so the panel cannot show a window title or a line of OCR even by
/// mistake, and neither can the clipboard text the Copy button produces (INV-10). The panel also decides
/// what to say when the service cannot be asked, so that is tested without a window.
///
/// While the window is open it asks the service again every <see cref="CoreShell.DiagnosticsPanel.RefreshEvery"/>
/// and <see cref="Show"/>s the answer, so CPU, memory and the capture row are live (ST-031). Only what
/// changed is raised, so the headline's live region speaks when the state changes and not on every refresh.
/// </summary>
public sealed partial class DiagnosticsViewModel : ObservableObject
{
    private CoreShell.DiagnosticsPanel _panel;
    private readonly Action<string> _copy;
    private readonly TimeProvider _time;

    public DiagnosticsViewModel(CoreShell.Diagnostics diagnostics, Action<string>? copy = null, TimeProvider? time = null)
        : this(CoreShell.DiagnosticsPanel.From(diagnostics ?? throw new ArgumentNullException(nameof(diagnostics))), copy, time)
    {
    }

    /// <param name="panel">The rows, answered by the service or saying unknown on every one when it could not be asked (T-152).</param>
    public DiagnosticsViewModel(CoreShell.DiagnosticsPanel panel, Action<string>? copy = null, TimeProvider? time = null)
    {
        _panel = panel ?? throw new ArgumentNullException(nameof(panel));
        _copy = copy ?? System.Windows.Clipboard.SetText;
        _time = time ?? TimeProvider.System;

        Headline = _panel.Headline;
        Rows = RowsOf(_panel);
        State = _panel.State;
        LocalOnlyVisibility = VisibilityOf(_panel);
        InPlaceOfRows = _panel.InPlaceOfRows ?? string.Empty;
        RowsVisibility = RowsVisibilityOf(_panel);
    }

    /// <summary>The one line said in place of the rows while the service has not answered (review finding B-55); empty once it has.</summary>
    [ObservableProperty]
    public partial string InPlaceOfRows { get; private set; }

    /// <summary>The rows card, hidden while the service has not answered, since every row would say unknown.</summary>
    [ObservableProperty]
    public partial System.Windows.Visibility RowsVisibility { get; private set; }

    private static System.Windows.Visibility RowsVisibilityOf(CoreShell.DiagnosticsPanel panel) =>
        panel.InPlaceOfRows is null ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    [ObservableProperty]
    public partial string Headline { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<DiagnosticsRow> Rows { get; private set; }

    /// <summary>The headline's state, which picks its glyph and colour (Spec §1.1): Capturing, Paused, NotCapturing or Unknown.</summary>
    [ObservableProperty]
    public partial CoreShell.DiagnosticsState State { get; private set; }

    /// <summary>Spec §5 S1's Local-only badge: shown when the service says local-only mode is on.</summary>
    [ObservableProperty]
    public partial System.Windows.Visibility LocalOnlyVisibility { get; private set; }

    /// <summary>
    /// The service's newer answer, or the unanswered panel when it stopped answering. The rows are swapped
    /// only when one of them says something different, so a refresh that changed nothing redraws nothing.
    /// What Copy puts on the clipboard follows: it is always the panel on screen.
    /// </summary>
    public void Show(CoreShell.DiagnosticsPanel panel)
    {
        _panel = panel ?? throw new ArgumentNullException(nameof(panel));
        Headline = panel.Headline;
        State = panel.State;
        LocalOnlyVisibility = VisibilityOf(panel);
        InPlaceOfRows = panel.InPlaceOfRows ?? string.Empty;
        RowsVisibility = RowsVisibilityOf(panel);
        var rows = RowsOf(panel);
        if (!rows.SequenceEqual(Rows))
        {
            Rows = rows;
        }
    }

    private static IReadOnlyList<DiagnosticsRow> RowsOf(CoreShell.DiagnosticsPanel panel) =>
        [.. panel.Rows.Select(line => new DiagnosticsRow(line.Label, line.Value, line.Kind))];

    private static System.Windows.Visibility VisibilityOf(CoreShell.DiagnosticsPanel panel) =>
        panel.LocalOnly == true ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    /// <summary>Said beside Copy diagnostics once it has run; empty, and so not shown, before.</summary>
    [ObservableProperty]
    public partial string Copied { get; set; } = string.Empty;

    [RelayCommand]
    private void Copy()
    {
        _copy(_panel.ToClipboardText(_time.GetUtcNow()));
        Copied = CoreShell.DiagnosticsPanel.Copied;
    }
}
