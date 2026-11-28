using System.Text.RegularExpressions;

namespace ScreenTrail.Tests.Ipc;

/// <summary>
/// The Windows pipe tests give every wait a time limit (security review B-01, fix forward 2).
///
/// Those tests only run on Windows, and the test run there takes them one at a time: one wait that never
/// ends holds up every test after it until the job is cancelled. A read over loopback SMB did exactly that,
/// and the tests behind it, among them the one that checks the capture pipe, never ran. So each wait in
/// these files is held here, on any OS, to one of the forms that end: a step through <c>PipeSteps</c>, a
/// <c>WaitAsync</c> with a time, a private helper of the same file (whose own waits are held the same way),
/// or a task a <c>PipeSteps</c> call returned. An await inside an interpolated string is not looked into,
/// so it is refused. <c>await using</c> is let through: disposing a pipe stream closes its handle and does
/// not wait for the far end to read or write.
/// </summary>
public sealed partial class PipeTestWaitsTests
{
    [Theory]
    [InlineData("client/ScreenTrail.Tests.Windows/Ipc/WindowsVerifierPipeTests.cs")]
    [InlineData("client/ScreenTrail.Tests.Windows/Ipc/WindowsPipeFactoryTests.cs")]
    [InlineData("client/ScreenTrail.Tests.Windows/Ipc/PillWindowTests.cs")]
    public void EveryWaitInTheWindowsPipeTestsHasATimeLimit(string file)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..");
        var unbounded = Unbounded(File.ReadAllText(Path.Combine(root, file))).ToList();

        Assert.True(unbounded.Count == 0, $"{file} waits with no time limit:\n  {string.Join("\n  ", unbounded)}");
    }

    [Theory]
    [InlineData("await pipe.ReadExactlyAsync(new byte[1], ct);")]
    [InlineData("await client.WriteAsync(new byte[] { 1 }, ct);")]
    [InlineData("await accepting;")]
    [InlineData("var check = await verifier.CheckAsync(pipe, DateTime.UtcNow, ct);")]
    [InlineData("await client.ConnectAsync(Soon, ct);")]
    [InlineData("await accepting.WaitAsync(ct);")]
    [InlineData("await accepting.WaitAsync(TestContext.Current.CancellationToken);")]
    [InlineData("Assert.Equal(\"x\", await new WindowsClientVerifier(self).VerifyAsync(client, ct));")]
    [InlineData("await using var client = await IpcClient.ConnectAsync(name, token, \"ui\", verifier, \"0.0.1\", Soon, ct);")]
    [InlineData("pipe.WaitForConnection();")]
    [InlineData("probe.Connect();")]
    [InlineData("task.Wait();")]
    [InlineData("await PipeSteps.Within(Timeout.InfiniteTimeSpan, \"the read\", t => pipe.ReadAsync(buffer, t).AsTask());")]
    [InlineData("Measurements.Record($\"seen: {await SeenAsync(pipe)}\");")]
    public void AWaitWithNoLimitIsFound(string statement)
    {
        Assert.Single(Unbounded(Wrapped(statement)));
    }

    [Theory]
    [InlineData("await PipeSteps.Within(Step, \"the read\", t => pipe.ReadAsync(buffer, t).AsTask());")]
    [InlineData("var check = await PipeSteps.Within(Step, \"the check\", t => verifier.CheckAsync(pipe, DateTime.UtcNow, t).AsTask());")]
    [InlineData("await accepting.WaitAsync(Soon, ct);")]
    [InlineData("await held.Task.WaitAsync(Soon, TestContext.Current.CancellationToken);")]
    [InlineData("await using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut);")]
    [InlineData("await squatter.DisposeAsync();")]
    [InlineData("// await pipe.ReadExactlyAsync(new byte[1], ct);")]
    [InlineData("Measurements.Record(\"we await nothing here\");")]
    [InlineData("probe.Connect(5_000);")]
    public void AWaitWithALimitPasses(string statement)
    {
        Assert.Empty(Unbounded(Wrapped(statement)));
    }

    [Fact]
    public void ATaskAPipeStepsCallReturnedAndAHelperOfTheFileMayBeAwaited()
    {
        var source = Wrapped(
            "var reading = PipeSteps.TryWithin(Step, t => pipe.ReadAsync(buffer, t).AsTask());\n"
            + "var (arrived, count) = await reading;\n"
            + "var serves = await LoopbackSmbServesAsync();\n"
            + "var other = Task.Delay(1);\n"
            + "await other;")
            + "\nprivate static async Task<bool> LoopbackSmbServesAsync() => await PipeSteps.TryWithin(Step, t => Task.Delay(1, t));\n";

        Assert.Equal(["await other"], Unbounded(source));
    }

    private static string Wrapped(string statement) => $"public async Task Test()\n{{\n    {statement}\n}}\n";

    /// <summary>Each await (and blocking wait) in <paramref name="source"/> that has no time limit, as it is written.</summary>
    internal static IEnumerable<string> Unbounded(string source)
    {
        // String literals are emptied below, so an await inside an interpolation would not be seen there.
        foreach (Match hidden in AwaitInInterpolation().Matches(source))
        {
            yield return $"an await inside an interpolated string: {hidden.Value}";
        }

        var code = Code(source);
        var helpers = HelperMethod().Matches(code).Select(m => m.Groups["name"].Value).ToHashSet(StringComparer.Ordinal);
        var stepTasks = StepTask().Matches(code).Select(m => m.Groups["name"].Value).ToHashSet(StringComparer.Ordinal);

        foreach (Match found in AwaitKeyword().Matches(code))
        {
            var expression = Expression(code, found.Index + found.Length);
            if (!Bounded(expression, helpers, stepTasks))
            {
                yield return $"await {expression}";
            }
        }

        foreach (Match blocking in BlockingWait().Matches(code))
        {
            yield return blocking.Value;
        }
    }

    private static bool Bounded(string expression, HashSet<string> helpers, HashSet<string> stepTasks)
    {
        if (expression.Contains("Infinite", StringComparison.Ordinal))
        {
            return false;
        }

        if (expression.StartsWith("PipeSteps.", StringComparison.Ordinal)
            || DisposeOnly().IsMatch(expression)
            || stepTasks.Contains(expression))
        {
            return true;
        }

        var call = LeadingCall().Match(expression);
        if (call.Success && helpers.Contains(call.Groups["name"].Value))
        {
            return true;
        }

        // A WaitAsync that is the last call of the expression, given a time and not only a token.
        var wait = expression.LastIndexOf(".WaitAsync(", StringComparison.Ordinal);
        if (wait < 0)
        {
            return false;
        }

        var open = wait + ".WaitAsync".Length;
        var close = Closing(expression, open);
        if (close != expression.Length - 1)
        {
            return false;
        }

        var arguments = expression[(open + 1)..close].Split(',');
        return arguments.Length >= 2 || !OnlyAToken().IsMatch(arguments[0].Trim());
    }

    /// <summary>The awaited expression: up to the end of the statement, or of the argument it stands in.</summary>
    private static string Expression(string code, int start)
    {
        var depth = 0;
        for (var i = start; i < code.Length; i++)
        {
            switch (code[i])
            {
                case '(' or '[' or '{':
                    depth++;
                    break;
                case ')' or ']' or '}':
                    if (--depth < 0)
                    {
                        return Squeeze(code[start..i]);
                    }

                    break;
                case ';' or ',' when depth == 0:
                    return Squeeze(code[start..i]);
            }
        }

        return Squeeze(code[start..]);
    }

    private static int Closing(string text, int open)
    {
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '(')
            {
                depth++;
            }
            else if (text[i] == ')' && --depth == 0)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The source without its comment lines and with every string literal emptied.</summary>
    private static string Code(string source)
    {
        var lines = source.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal));
        return StringLiteral().Replace(string.Join('\n', lines), "\"\"");
    }

    private static string Squeeze(string text) => Whitespace().Replace(text, " ").Trim();

    [GeneratedRegex(@"\bawait\s+(?!using\b|foreach\b)")]
    private static partial Regex AwaitKeyword();

    [GeneratedRegex(@"\.(?:Wait|WaitForConnection|Connect|WaitForExit)\(\)|\.GetAwaiter\(\)\.GetResult\(\)")]
    private static partial Regex BlockingWait();

    [GeneratedRegex(@"private\s+static\s+async\s+Task(?:<[^>]+>)?\s+(?<name>\w+)\(")]
    private static partial Regex HelperMethod();

    [GeneratedRegex(@"var\s+(?<name>\w+)\s*=\s*PipeSteps\.")]
    private static partial Regex StepTask();

    [GeneratedRegex(@"^(?<name>\w+)\(")]
    private static partial Regex LeadingCall();

    [GeneratedRegex(@"^\w+\.DisposeAsync\(\)$")]
    private static partial Regex DisposeOnly();

    [GeneratedRegex(@"^(?:ct|[\w.]*Token)$")]
    private static partial Regex OnlyAToken();

    [GeneratedRegex(@"""(?:[^""\\\n]|\\.)*""")]
    private static partial Regex StringLiteral();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"\$@?""[^""\n]*\{[^}\n]*\bawait\b[^}\n]*\}")]
    private static partial Regex AwaitInInterpolation();
}
