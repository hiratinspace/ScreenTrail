using System.Text.Json;
using ScreenTrail.Core.Ipc;
using ScreenTrail.Core.Perf;
using ScreenTrail.Core.Shell;
using ScreenTrail.Shared.Ipc;
using ScreenTrail.Shared.Schema;

namespace ScreenTrail.Tests.Shell;

/// <summary>
/// ST-031's live figures in "What's being captured right now?": the service's real share of the PC
/// where a 0 was always sent, its memory, and, when capture has given way to keep the PC responsive, a
/// row saying so in plain words. Numbers and a level only (INV-10).
/// </summary>
public sealed class DiagnosticsPanelLiveFiguresTests
{
    [Fact]
    public void TheProcessorRowIsTheServicesShareOfThisPc()
    {
        var panel = DiagnosticsPanel.From(With(cpuPercent: 3.4));

        Assert.Equal("3.4% of this PC", Row(panel, DiagnosticsPanel.Cpu));
        Assert.Equal("240 MB", Row(panel, DiagnosticsPanel.Memory));
    }

    [Fact]
    public void BeforeThereIsAReadingTheProcessorRowSaysMeasuringNotZero()
    {
        // The service sent 0 here for every answer it ever gave; a 0 is a claim, and nobody had measured.
        var panel = DiagnosticsPanel.From(With(cpuPercent: null));

        Assert.Equal(DiagnosticsPanel.Measuring, Row(panel, DiagnosticsPanel.Cpu));
        Assert.Contains("cpu              measuring", panel.ToClipboardText(DateTimeOffset.UnixEpoch), StringComparison.Ordinal);
    }

    [Fact]
    public void TheFigureIsCountedTheWayTaskManagerCountsIt()
    {
        Assert.Equal("0.0% of this PC", DiagnosticsPanel.CpuValue(0));
        Assert.Equal("12.5% of this PC", DiagnosticsPanel.CpuValue(12.5));
        Assert.Equal("100.0% of this PC", DiagnosticsPanel.CpuValue(100));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(CaptureLevels.Full)]
    public void AtFullCaptureThereIsNoCaptureRow(string? level)
    {
        var panel = DiagnosticsPanel.From(With(captureLevel: level));

        Assert.DoesNotContain(panel.Rows, r => r.Label == DiagnosticsPanel.Capture);
    }

    [Theory]
    [InlineData(CaptureLevels.FewerSamples, "Taking fewer screenshots between clicks to keep your PC responsive")]
    [InlineData(CaptureLevels.FewestSamples, "Taking far fewer screenshots between clicks to keep your PC responsive")]
    [InlineData(CaptureLevels.OcrOneAtATime, "Taking far fewer screenshots between clicks and checking them one at a time to keep your PC responsive")]
    [InlineData(CaptureLevels.OcrClicksOnly, "Taking screenshots only on clicks and marked moments to keep your PC responsive")]
    public void WhenCaptureHasGivenWayTheRowUnderTheScopeSaysWhatInPlainWords(string level, string words)
    {
        var panel = DiagnosticsPanel.From(With(captureLevel: level));

        // Second, under the scope, where a customer reading over a shoulder sees it with what is captured.
        Assert.Equal(new DiagnosticsLine(DiagnosticsPanel.Capture, words), panel.Rows[1]);
        Assert.Contains("capture          " + words, panel.ToClipboardText(DateTimeOffset.UnixEpoch), StringComparison.Ordinal);
    }

    [Fact]
    public void NoLevelIsSaidInTheThrottlesOwnNames()
    {
        // "OcrClicksOnly" means nothing to a technician or a customer.
        foreach (var level in Enum.GetValues<CaptureLevel>().Where(l => l != CaptureLevel.Full))
        {
            var words = DiagnosticsPanel.CaptureWords(CaptureLevelNames.WireName(level));

            Assert.NotNull(words);
            Assert.DoesNotContain("_", words, StringComparison.Ordinal);
            Assert.DoesNotContain("ocr", words, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("throttl", words, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("sampl", words, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("—", words, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ALevelThisBuildDoesNotKnowStillSaysCaptureEasedOff()
    {
        Assert.Equal("Taking fewer screenshots to keep your PC responsive", DiagnosticsPanel.CaptureWords("a_new_level"));
    }

    [Fact]
    public void TheCopiedTextSaysCaptureIsAsUsualAtFullAndSaysNothingWhenTheServiceDidNot()
    {
        Assert.Contains("capture          as usual", DiagnosticsPanel.From(With(captureLevel: CaptureLevels.Full)).ToClipboardText(DateTimeOffset.UnixEpoch), StringComparison.Ordinal);
        Assert.DoesNotContain("capture ", DiagnosticsPanel.From(With(captureLevel: null)).ToClipboardText(DateTimeOffset.UnixEpoch), StringComparison.Ordinal);
    }

    [Fact]
    public void UnansweredTheFiguresAreUnknownAndThereIsNoCaptureRow()
    {
        var panel = DiagnosticsPanel.FromReply(null);

        Assert.Same(DiagnosticsPanel.Unanswered, panel);
        Assert.Equal(DiagnosticsPanel.Unknown, Row(panel, DiagnosticsPanel.Cpu));
        Assert.Equal(DiagnosticsPanel.Unknown, Row(panel, DiagnosticsPanel.Memory));
        Assert.DoesNotContain(panel.Rows, r => r.Label == DiagnosticsPanel.Capture);
    }

    [Fact]
    public void TheServiceSendsItsShareAsAPercentageAndItsLevelByName()
    {
        var reported = Report(cpuShare: 0.0342, captureLevel: CaptureLevelNames.WireName(CaptureLevel.FewerSamples));
        var json = JsonSerializer.Serialize<IpcEvent>(reported, IpcFraming.Json);

        Assert.Equal(3.4, reported.CpuPercent);
        Assert.Contains("\"cpu_percent\":3.4", json, StringComparison.Ordinal);
        Assert.Contains("\"capture_level\":\"fewer_samples\"", json, StringComparison.Ordinal);
        var panel = DiagnosticsPanel.FromReply(Assert.IsType<DiagnosticsReported>(JsonSerializer.Deserialize<IpcEvent>(json, IpcFraming.Json)));
        Assert.Equal("3.4% of this PC", Row(panel, DiagnosticsPanel.Cpu));
        Assert.Equal(DiagnosticsPanel.CaptureWords(CaptureLevels.FewerSamples), Row(panel, DiagnosticsPanel.Capture));
    }

    [Fact]
    public void WithNoReadingYetTheServiceLeavesTheFigureOutRatherThanSendingZero()
    {
        var json = JsonSerializer.Serialize<IpcEvent>(Report(cpuShare: null, captureLevel: null), IpcFraming.Json);

        Assert.DoesNotContain("cpu_percent", json, StringComparison.Ordinal);
        Assert.DoesNotContain("capture_level", json, StringComparison.Ordinal);
    }

    private static string Row(DiagnosticsPanel panel, string label) => panel.Rows.Single(r => r.Label == label).Value;

    private static DiagnosticsReported Report(double? cpuShare, string? captureLevel) => ServiceDiagnostics.Report(
        serviceVersion: "0.1.0",
        scope: null,
        microphone: null,
        CaptureStateSnapshot.Idle,
        isHeld: _ => false,
        redactionBacklog: 0,
        redaction: null,
        framesRefused: 0,
        keystrokesDropped: 0,
        egressBlocked: 0,
        nameHookRefusals: 0,
        localOnly: true,
        policyVersion: "default",
        workingSetBytes: 240L * 1024 * 1024,
        cpuShare: cpuShare,
        captureLevel: captureLevel);

    private static Diagnostics With(double? cpuPercent = 1.5, string? captureLevel = null) => new(
        Scope: "Capturing: ScreenConnect",
        Microphone: "Headset Microphone",
        Suppression: null,
        RedactionBacklog: 0,
        FramesDropped: 0,
        KeystrokesDropped: 0,
        EgressBlocked: 0,
        NameHookRefusals: 0,
        LocalOnly: true,
        PolicyVersion: "policy-1",
        CpuPercent: cpuPercent,
        WorkingSetBytes: 240L * 1024 * 1024,
        ClientVersion: "0.1.0",
        CaptureLevel: captureLevel);
}
