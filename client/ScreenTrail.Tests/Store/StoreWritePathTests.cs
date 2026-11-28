using System.Reflection;
using ScreenTrail.Core.Store;

namespace ScreenTrail.Tests.Store;

/// <summary>
/// What the store's own surface lets a caller do with a frame (INV-1, ADR-0006; review finding A-04).
///
/// A captured frame waits in memory and is written once, already read and masked, by
/// <see cref="ISessionStore.SaveRedactedFrameAsync"/>. A store that could also take a frame on its own,
/// with no redaction beside it, or hand a stored image back before redaction, or apply a redaction to a
/// row already on disk, would be a second way for captured pixels to reach the file, whoever calls it.
/// So the rule is checked on the types rather than on today's callers: on <see cref="ISessionStore"/> and
/// on every type in Core that implements it, public or not. Every read and write the store makes is
/// asynchronous, so its operations are the methods that return a task; a synchronous helper that only
/// works out a value (a stored size, say) is not one.
/// </summary>
public sealed class StoreWritePathTests
{
    private const BindingFlags Declared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    [Fact]
    public void AFrameReachesTheStoreOnlyWithItsRedaction()
    {
        var offending = StoreTypes()
            .SelectMany(type => type.GetMethods(Declared).Select(method => (type, method)))
            .Where(entry => !entry.method.IsSpecialName && IsOperation(entry.method))
            .Select(entry => (entry.type, entry.method, why: Why(entry.method)))
            .Where(entry => entry.why is not null)
            .Select(entry => $"{entry.type.Name}.{entry.method.Name}: {entry.why}")
            .Distinct()
            .ToList();

        Assert.True(offending.Count == 0, "The store offers a frame path without redaction:\n" + string.Join("\n", offending));
    }

    [Fact]
    public void TheCheckSeesTheStoreAndItsWrite()
    {
        // So the test above cannot pass by finding nothing to look at.
        Assert.Contains(typeof(SqliteSessionStore), StoreTypes());
        var write = typeof(ISessionStore).GetMethod(nameof(ISessionStore.SaveRedactedFrameAsync))!;
        Assert.True(IsOperation(write));
        Assert.Null(Why(write));
    }

    private static IEnumerable<Type> StoreTypes() =>
        typeof(ISessionStore).Assembly.GetTypes().Where(type => typeof(ISessionStore).IsAssignableFrom(type));

    private static string? Why(MethodInfo method)
    {
        var parameters = method.GetParameters().Select(parameter => parameter.ParameterType).ToList();
        var takesFrame = parameters.Contains(typeof(StagedFrame));
        var takesRedaction = parameters.Contains(typeof(RedactionOutcome));

        if (takesFrame && !takesRedaction)
        {
            return "takes a captured frame without its redaction";
        }

        if (takesRedaction && !takesFrame)
        {
            return "applies a redaction to a frame already stored";
        }

        return Mentions(method.ReturnType, typeof(PendingFrame))
            ? "hands back a frame that has not been redacted"
            : null;
    }

    private static bool IsOperation(MethodInfo method) =>
        typeof(Task).IsAssignableFrom(method.ReturnType)
        || method.ReturnType == typeof(ValueTask)
        || (method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(ValueTask<>));

    private static bool Mentions(Type type, Type wanted) =>
        type == wanted
        || (type.IsGenericType && type.GetGenericArguments().Any(argument => Mentions(argument, wanted)));
}
