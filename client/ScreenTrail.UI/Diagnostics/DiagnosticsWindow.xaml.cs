using System.Windows;
using System.Windows.Threading;
using ScreenTrail.Core.Shell;
using ScreenTrail.Shared.Ipc;
using CoreShell = ScreenTrail.Core.Shell;

namespace ScreenTrail.UI.Diagnostics;

public partial class DiagnosticsWindow : Window
{
    /// <summary>
    /// The CI render only. A panel showing a literal is what this window used to do in production, and a
    /// panel that tells a customer "local-only: yes" from a constant is worse than no panel at all
    /// (weaknesses P1-2). <see cref="ShowLiveAsync"/> is what the application uses.
    /// </summary>
    public DiagnosticsWindow()
        : this(Sample)
    {
    }

    /// <summary>
    /// Opens the panel with what the service says, or saying unknown on every row when it cannot be asked
    /// (ST-085, T-152).
    ///
    /// The question is asked every time the panel opens rather than cached, because the answer is what a
    /// technician turns the screen round and shows a customer, and a stale one is a claim about the
    /// present tense that happens to be about the past. For the same reason it is asked again every
    /// <see cref="CoreShell.DiagnosticsPanel.RefreshEvery"/> while the panel is open, so CPU, memory and
    /// whether capture has eased off are live (ST-031), and not at all once it is closed.
    /// </summary>
    public static async Task ShowLiveAsync(CaptureConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var window = new DiagnosticsWindow(await AskAsync(connection).ConfigureAwait(true));
        window.Refresh(connection);
        window.Show();
        _ = window.Activate();
    }

    private static async Task<CoreShell.DiagnosticsPanel> AskAsync(CaptureConnection connection, CancellationToken ct = default) =>
        CoreShell.DiagnosticsPanel.FromReply(await connection.RequestAsync<DiagnosticsReported>(
            id => new GetDiagnosticsCommand { RequestId = id }, ct).ConfigureAwait(true));

    /// <summary>
    /// Asks again on a timer while the window is open. One question at a time: a tick that finds the last
    /// still unanswered waits for the next, and one not answered within a few seconds shows the panel as
    /// unanswered rather than leaving old figures up as if they were now. Stopped when the window closes.
    /// </summary>
    private void Refresh(CaptureConnection connection)
    {
        var asking = false;
        var timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = CoreShell.DiagnosticsPanel.RefreshEvery };
        timer.Tick += async (_, _) =>
        {
            if (asking)
            {
                return;
            }

            asking = true;
            try
            {
                CoreShell.DiagnosticsPanel panel;
                using (var patience = new CancellationTokenSource(CoreShell.DiagnosticsPanel.RefreshEvery * 2))
                {
                    try
                    {
                        panel = await AskAsync(connection, patience.Token).ConfigureAwait(true);
                    }
                    catch (OperationCanceledException)
                    {
                        panel = CoreShell.DiagnosticsPanel.Unanswered;
                    }
                }

                if (timer.IsEnabled)
                {
                    ((DiagnosticsViewModel)DataContext).Show(panel);
                }
            }
            finally
            {
                asking = false;
            }
        };
        Closed += (_, _) => timer.Stop();
        timer.Start();
    }

    public DiagnosticsWindow(CoreShell.Diagnostics diagnostics)
        : this(CoreShell.DiagnosticsPanel.From(diagnostics))
    {
    }

    /// <summary>
    /// The panel as <see cref="CoreShell.DiagnosticsPanel"/> decided it, which is what says "unknown" on
    /// every row when the service is not answering. Every count used to be shown as zero and local-only
    /// as "no", from a literal in this file (2026-10-12 review, T-152).
    /// </summary>
    public DiagnosticsWindow(CoreShell.DiagnosticsPanel panel)
    {
        InitializeComponent();
        Theme.FitToWorkArea.Apply(this);
        DataContext = new DiagnosticsViewModel(panel);
    }

    /// <summary>Close, which Esc presses too (IsCancel), as on the shortcut sheet and in the dialog.</summary>
    private void OnClose(object sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// What the panel shows before anything has connected, and what the CI render draws. Every value is
    /// the shape of a real one so the screenshot shows the panel doing its job; a blank one would pass
    /// the render check while proving nothing.
    /// </summary>
    public static CoreShell.Diagnostics Sample { get; } = new(
        Scope: "Capturing: ScreenConnect",
        Microphone: "Headset Microphone (Realtek)",
        Suppression: null,
        RedactionBacklog: 4,
        FramesDropped: 0,
        KeystrokesDropped: 17,
        EgressBlocked: 0,
        // Above zero so the render and the accessibility scan see the row with its explanation, the longest it gets.
        NameHookRefusals: 1,
        LocalOnly: true,
        PolicyVersion: "policy-2026-09-13",
        CpuPercent: 3.4,
        WorkingSetBytes: 240L * 1024 * 1024,
        ClientVersion: "0.1.0",
        // Eased off at its longest words, so the render and the accessibility scan see the capture row (ST-031).
        CaptureLevel: CaptureLevels.OcrOneAtATime);
}
