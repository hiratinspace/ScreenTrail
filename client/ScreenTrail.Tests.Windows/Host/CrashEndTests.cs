using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ScreenTrail.Core.Lifecycle;
using ScreenTrail.Platform.Lifecycle;

namespace ScreenTrail.Tests.Windows.Host;

/// <summary>
/// A-08 on Windows: a process with ScreenTrail's crash handler that throws on a thread nobody owns ends
/// itself with <see cref="CrashEnd.ExitCode"/>, and the Application event log gets no entry holding the
/// exception's message. The child is this suite's own process, started again to run only the throw.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CrashEndTests
{
    private const string ChildRoleVariable = "SCREENTRAIL_TESTS_CHILD";

    private const string ChildRole = "crash-with-a-marker";

    private const string MarkerVariable = "SCREENTRAIL_TESTS_CRASH_MARKER";

    [Fact(Timeout = 180_000)]
    public async Task ACrashOnAThreadNobodyOwnsLeavesNoEventWithItsMessage()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only.");
        if (Environment.GetEnvironmentVariable(ChildRoleVariable) == ChildRole)
        {
            CrashAsChild();
            return;
        }

        var ct = TestContext.Current.CancellationToken;
        var marker = "A08-MARKER-" + Guid.NewGuid().ToString("N");
        var started = DateTime.UtcNow.AddSeconds(-2);
        using var child = Process.Start(ChildStart(marker)) ?? throw new InvalidOperationException("The child test process did not start.");
        try
        {
            using var waiting = CancellationTokenSource.CreateLinkedTokenSource(ct);
            waiting.CancelAfter(TimeSpan.FromSeconds(90));
            await child.WaitForExitAsync(waiting.Token);
        }
        finally
        {
            if (!child.HasExited)
            {
                child.Kill(entireProcessTree: true);
            }
        }

        Assert.Equal(CrashEnd.ExitCode, child.ExitCode);

        // The runtime writes its entry as the process goes, so a moment is allowed for it to appear.
        await Task.Delay(TimeSpan.FromSeconds(5), ct);
        var query = new EventLogQuery(
            "Application",
            PathType.LogName,
            string.Create(CultureInfo.InvariantCulture, $"*[System[TimeCreated[@SystemTime>='{started:yyyy-MM-ddTHH:mm:ss.fffZ}']]]"));
        using var reader = new EventLogReader(query);
        for (var record = reader.ReadEvent(); record is not null; record = reader.ReadEvent())
        {
            using (record)
            {
                var text = Describe(record);
                Assert.False(text.Contains(marker, StringComparison.Ordinal), $"An Application-log event from {record.ProviderName} ({record.Id}) holds the crash's message.");
            }
        }
    }

    private static string Describe(EventRecord record)
    {
        string? description = null;
        try
        {
            description = record.FormatDescription();
        }
        catch (EventLogException)
        {
            // A provider whose messages are not installed: its properties are read below.
        }

        return string.Join('\n', [description ?? string.Empty, .. record.Properties.Select(p => p.Value?.ToString() ?? string.Empty)]);
    }

    /// <summary>The child's half: the product's handler, then a throw on a thread of its own.</summary>
    private static void CrashAsChild()
    {
        var marker = Environment.GetEnvironmentVariable(MarkerVariable) ?? throw new InvalidOperationException("No marker was named.");
        ProcessEnd.Install(_ => { });
        var thread = new Thread(() => throw new InvalidOperationException(marker)) { IsBackground = false };
        thread.Start();
        thread.Join();
        Thread.Sleep(TimeSpan.FromSeconds(60));
    }

    private static ProcessStartInfo ChildStart(string marker)
    {
        var suite = typeof(CrashEndTests).Assembly;
        var current = Environment.ProcessPath;
        var asItself = current is not null && string.Equals(Path.GetFileNameWithoutExtension(current), suite.GetName().Name, StringComparison.OrdinalIgnoreCase);
        var start = new ProcessStartInfo(asItself ? current! : DotnetHost(current))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (!asItself)
        {
            start.ArgumentList.Add(suite.Location);
        }

        start.ArgumentList.Add("-method");
        start.ArgumentList.Add($"{typeof(CrashEndTests).FullName}.{nameof(ACrashOnAThreadNobodyOwnsLeavesNoEventWithItsMessage)}");
        start.ArgumentList.Add("-nologo");
        start.Environment[ChildRoleVariable] = ChildRole;
        start.Environment[MarkerVariable] = marker;
        return start;
    }

    private static string DotnetHost(string? current)
    {
        if (current is not null && string.Equals(Path.GetFileNameWithoutExtension(current), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return current;
        }

        if (Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } named && File.Exists(named))
        {
            return named;
        }

        var runtime = new DirectoryInfo(RuntimeEnvironment.GetRuntimeDirectory());
        if (runtime.Parent?.Parent?.Parent is { } root && Path.Combine(root.FullName, "dotnet.exe") is var beside && File.Exists(beside))
        {
            return beside;
        }

        return "dotnet";
    }
}
