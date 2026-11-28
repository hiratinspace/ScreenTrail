using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using ScreenTrail.Core.Privacy;
using ScreenTrail.Service.Privacy;

namespace ScreenTrail.Tests.Windows.Privacy;

/// <summary>
/// A-05 (b), measured before it is built (MC-a2): how often, and for how long in a row, the focused-field
/// probe gives no answer, so that a rule which takes a hold after a run of unanswered probes can be given
/// a bound ordinary work never reaches. A measurement, not a floor: it prints what it saw and asserts
/// nothing about what the product does.
///
/// Without <c>SCREENTRAIL_FOCUS_SURVEY_SECONDS</c> it asks about the test's own window only (a plain box, a
/// password box, and the window itself, which is not an edit control), so a run anywhere says what the two
/// mechanisms answer there. With it, it asks once a second for that many seconds, the guard's own backstop
/// rate, about whatever has focus while the owner works, and tallies the answers by the process in front.
/// Process names only, never titles, and only in that opt-in run on the owner's own machine.
/// </summary>
public sealed class FocusedFieldSurveyTests
{
    private const string SurveyVariable = "SCREENTRAIL_FOCUS_SURVEY_SECONDS";

    [Fact]
    public void HowOftenTheProbeCannotSayIsMeasured()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only.");
        var seconds = int.TryParse(Environment.GetEnvironmentVariable(SurveyVariable), NumberStyles.Integer, CultureInfo.InvariantCulture, out var asked) && asked > 0
            ? asked
            : 0;

        if (seconds == 0)
        {
            MeasureOwnWindow();
            return;
        }

        Survey(TimeSpan.FromSeconds(seconds), TestContext.Current.CancellationToken);
    }

    private static void MeasureOwnWindow()
    {
        using var window = DesktopWindow.CreateOrdinary("ScreenTrail focus survey");
        window.AddFields();
        window.RequireForeground();
        using var probe = new WindowsFocusedFieldProbe();
        _ = probe.Read();   // the first call creates the automation object

        var lines = new List<string>
        {
            "Focused-field probe on the test's own window, 10 asks each (A-05 (b), MC-a2):",
            "| focus | answers | by | longest run of no answer |",
            "|---|---|---|---|",
        };
        foreach (var (name, control) in new[] { ("plain box", window.PlainField), ("password box", window.PasswordField), ("the window itself", window.Handle) })
        {
            window.Focus(control);
            var tally = new Tally();
            for (var i = 0; i < 10; i++)
            {
                var start = Stopwatch.GetTimestamp();
                var answer = probe.Read();
                tally.Add(answer, probe.LastAnswerFrom, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            }

            lines.Add($"| {name} | {tally.Answers()} | {tally.Sources()} | {tally.LongestUnknownRun} |");
        }

        lines.Add($"Set {SurveyVariable} to a number of seconds and work normally to survey real windows.");
        Measurements.Record(string.Join(Environment.NewLine, lines));
    }

    private static void Survey(TimeSpan length, CancellationToken ct)
    {
        using var probe = new WindowsFocusedFieldProbe();
        var byProcess = new SortedDictionary<string, Tally>(StringComparer.OrdinalIgnoreCase);
        var overall = new Tally();
        var end = Stopwatch.GetTimestamp() + (long)(length.TotalSeconds * Stopwatch.Frequency);

        while (Stopwatch.GetTimestamp() < end && !ct.IsCancellationRequested)
        {
            var process = ProcessInFront();
            var start = Stopwatch.GetTimestamp();
            var answer = probe.Read();
            var took = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

            if (!byProcess.TryGetValue(process, out var tally))
            {
                tally = new Tally();
                byProcess[process] = tally;
            }

            tally.Add(answer, probe.LastAnswerFrom, took);
            overall.Add(answer, probe.LastAnswerFrom, took);

            // Once a second, as PasswordFieldOptions.PollEvery asks when no focus event arrives.
            var wait = TimeSpan.FromSeconds(1) - Stopwatch.GetElapsedTime(start);
            if (wait > TimeSpan.Zero)
            {
                _ = ct.WaitHandle.WaitOne(wait);
            }
        }

        var lines = new List<string>
        {
            $"Focused-field survey, one ask a second for {length.TotalSeconds:F0} s (A-05 (b), MC-a2). A run of no answer while focus was in an ordinary field is what a hold after N unknowns would turn into a suppression; N must be above the longest such run on every remote tool's window.",
            "| process in front | asks | answers | by | longest run of no answer | runs of 2+ / 3+ / 5+ / 10+ | ask median / p95 / worst ms |",
            "|---|---|---|---|---|---|---|",
        };
        foreach (var (process, tally) in byProcess)
        {
            lines.Add(tally.Row(process));
        }

        lines.Add(overall.Row("all"));
        Measurements.Record(string.Join(Environment.NewLine, lines));
    }

    /// <summary>The process name of the window in front, "none" when nothing is, "unknown" when Windows will not say.</summary>
    private static string ProcessInFront()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero)
        {
            return "none";
        }

        _ = GetWindowThreadProcessId(window, out var processId);
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return "unknown";
        }
    }

    private sealed class Tally
    {
        private readonly Dictionary<FocusedField, int> _answers = [];
        private readonly Dictionary<FocusAnswerSource, int> _sources = [];
        private readonly List<double> _times = [];
        private readonly int[] _runsReaching = new int[4];
        private static readonly int[] RunLengths = [2, 3, 5, 10];
        private int _run;

        public int LongestUnknownRun { get; private set; }

        public void Add(FocusedField answer, FocusAnswerSource source, double ms)
        {
            _answers[answer] = _answers.GetValueOrDefault(answer) + 1;
            _sources[source] = _sources.GetValueOrDefault(source) + 1;
            _times.Add(ms);

            if (answer != FocusedField.Unknown)
            {
                _run = 0;
                return;
            }

            _run++;
            LongestUnknownRun = Math.Max(LongestUnknownRun, _run);
            for (var i = 0; i < RunLengths.Length; i++)
            {
                if (_run == RunLengths[i])
                {
                    _runsReaching[i]++;
                }
            }
        }

        public string Answers() => string.Join(", ", _answers.OrderBy(a => a.Key).Select(a => $"{a.Key} {a.Value}"));

        public string Sources() => string.Join(", ", _sources.OrderBy(s => s.Key).Select(s => $"{s.Key} {s.Value}"));

        public string Row(string name)
        {
            var sorted = _times.Order().ToList();
            var median = sorted[sorted.Count / 2];
            var p95 = sorted[Math.Min(sorted.Count - 1, (int)Math.Ceiling(sorted.Count * 0.95) - 1)];
            return $"| {name} | {_times.Count} | {Answers()} | {Sources()} | {LongestUnknownRun} | "
                + $"{string.Join(" / ", _runsReaching)} | {median:F1} / {p95:F1} / {sorted[^1]:F1} |";
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}
