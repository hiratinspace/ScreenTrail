using System.Globalization;
using System.Text.RegularExpressions;
using ScreenTrail.Api.Auth;

namespace ScreenTrail.Api.Tests.Hosting;

/// <summary>
/// <c>deploy/smoke.sh</c> proves a client cannot choose its own rate-limit partition by naming a different
/// <c>X-Forwarded-For</c> on every call and expecting a 429 (ST-007, ADR-0009). That proves something only
/// if the loop sends more calls than the route it uses allows, so the number the script sends is checked
/// here against the backend's own count for the sign-in nonce (<see cref="AnonymousRateLimit.NonceMultiple"/>
/// times the <c>RateLimit__PermitLimit</c> the smoke gives the API), not left to be found stale in CI.
/// </summary>
public sealed partial class DeploySmokeTests
{
    [Fact]
    public void TheForwardedAddressCheckSendsMoreCallsThanTheNonceAllows()
    {
        var root = RepositoryRoot();
        var smoke = File.ReadAllLines(Path.Combine(root, "deploy", "smoke.sh"));

        // The count the API in the smoke run gives the nonce: the example the script copies to api.env, or
        // the default when the example leaves it out.
        var permits = File.ReadLines(Path.Combine(root, "deploy", "api.env.example"))
            .Select(l => EnvPermitLimit().Match(l))
            .Where(m => m.Success)
            .Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
            .DefaultIfEmpty(new RateLimitOptions().PermitLimit)
            .Single();
        var nonceCount = permits * AnonymousRateLimit.NonceMultiple;

        var call = Array.FindIndex(smoke, l => l.Contains("X-Forwarded-For", StringComparison.Ordinal) && l.Contains("/v1/admin/session/nonce", StringComparison.Ordinal));
        Assert.True(call > 0, "deploy/smoke.sh no longer sends the nonce calls that name a forwarded address");
        var loop = Array.FindLastIndex(smoke, call, l => SeqLoop().IsMatch(l));
        Assert.True(loop >= 0 && call - loop <= 3, "the nonce call in deploy/smoke.sh is not inside a `for i in $(seq 1 ...)` loop");

        var calls = Evaluate(SeqLoop().Match(smoke[loop]).Groups[1].Value, smoke, root);

        Assert.True(calls > nonceCount, $"deploy/smoke.sh sends {calls} nonce calls naming forwarded addresses, but the nonce allows {nonceCount} a minute ({permits} x AnonymousRateLimit.NonceMultiple {AnonymousRateLimit.NonceMultiple}): no 429 would not show the client chose its partition");
    }

    /// <summary>
    /// The loop's bound as the script works it out: a number, or a variable set to
    /// <c>$(( a * b + n ))</c> whose factors are each read with <c>sed -nE 's/RE/\1/p' "FILE"</c>.
    /// </summary>
    private static int Evaluate(string bound, string[] smoke, string root)
    {
        if (int.TryParse(bound, NumberStyles.None, CultureInfo.InvariantCulture, out var literal))
        {
            return literal;
        }

        var name = Variable().Match(bound);
        Assert.True(name.Success, $"the nonce loop's bound '{bound}' is neither a number nor a variable");
        var sum = Assigned(smoke, name.Groups[1].Value, SumPattern());
        return (Factor(smoke, root, sum.Groups[1].Value) * Factor(smoke, root, sum.Groups[2].Value))
            + int.Parse(sum.Groups[3].Value, CultureInfo.InvariantCulture);
    }

    private static int Factor(string[] smoke, string root, string name)
    {
        var read = Assigned(smoke, name, SedPattern());
        var file = read.Groups[2].Value switch
        {
            // The script copies the example to the scratch folder before it reads it there.
            "$scratch/api.env" => Path.Combine(root, "deploy", "api.env.example"),
            var path when path.StartsWith("$repo/", StringComparison.Ordinal) => Path.Combine(root, path["$repo/".Length..]),
            var path => throw new InvalidOperationException($"deploy/smoke.sh reads {name} from {path}, which this test cannot find"),
        };

        // An extended regular expression of this shape means the same to sed -E and to .NET.
        var values = File.ReadLines(file)
            .Select(l => Regex.Match(l, read.Groups[1].Value, RegexOptions.None, TimeSpan.FromSeconds(1)))
            .Where(m => m.Success)
            .Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
            .ToList();
        Assert.True(values.Count == 1, $"deploy/smoke.sh's {name} matches {values.Count} lines of {file}, not one");

        if (file.EndsWith("AnonymousRateLimit.cs", StringComparison.Ordinal))
        {
            Assert.Equal(AnonymousRateLimit.NonceMultiple, values[0]);
        }

        return values[0];
    }

    private static Match Assigned(string[] smoke, string name, Regex shape)
    {
        var found = smoke.Select(l => shape.Match(l.Trim())).Where(m => m.Success && m.Groups["name"].Value == name).ToList();
        Assert.True(found.Count == 1, $"deploy/smoke.sh sets {name} {found.Count} times in the expected form, not once");
        return found[0];
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "deploy", "smoke.sh")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("deploy/smoke.sh was not found above the test's directory.");
    }

    [GeneratedRegex(@"^RateLimit__PermitLimit=([0-9]+)$")]
    private static partial Regex EnvPermitLimit();

    [GeneratedRegex(@"^\s*for i in \$\(seq 1 ([^)]+)\); do")]
    private static partial Regex SeqLoop();

    [GeneratedRegex(@"^""?\$\{?([a-z_]+)\}?""?$")]
    private static partial Regex Variable();

    [GeneratedRegex(@"^(?:local )?(?<name>[a-z_]+)=\$\(\( *\$?([a-z_]+) *\* *\$?([a-z_]+) *\+ *([0-9]+) *\)\)$")]
    private static partial Regex SumPattern();

    [GeneratedRegex(@"^(?:local )?(?<name>[a-z_]+)=\$\(sed -nE 's/(.+)/\\1/p' ""([^""]+)""\)$")]
    private static partial Regex SedPattern();
}
