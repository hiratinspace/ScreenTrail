using ScreenTrail.Shared.Ipc;

namespace ScreenTrail.Core.Perf;

/// <summary>The throttle's level as the <c>diagnostics</c> reply names it (contract v15, <see cref="CaptureLevels"/>).</summary>
public static class CaptureLevelNames
{
    public static string WireName(CaptureLevel level) => level switch
    {
        CaptureLevel.Full => CaptureLevels.Full,
        CaptureLevel.FewerSamples => CaptureLevels.FewerSamples,
        CaptureLevel.FewestSamples => CaptureLevels.FewestSamples,
        CaptureLevel.OcrOneAtATime => CaptureLevels.OcrOneAtATime,
        CaptureLevel.OcrClicksOnly => CaptureLevels.OcrClicksOnly,
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "A level with no wire name."),
    };
}
