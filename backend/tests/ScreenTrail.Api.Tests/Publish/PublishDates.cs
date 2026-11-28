using System.Globalization;
using System.Text.Json.Nodes;

namespace ScreenTrail.Api.Tests.Publish;

/// <summary>
/// When the publish tests' sessions started: 14:02 UTC three days before the test run began, worked
/// out once per run so every bundle and every assertion in it agree. A publish's time entry may start
/// at most 365 days before the server's clock (E-02), so a written date would pass until it was a year
/// old and then fail; <see cref="PublishDatesGuardTests"/> keeps one from coming back. The fake clocks
/// the tests use start at the real time, so this is inside their window too.
/// </summary>
internal static class PublishDates
{
    public static readonly DateTimeOffset StartedAt =
        new(DateTime.UtcNow.Date.AddDays(-3).AddHours(14).AddMinutes(2), TimeSpan.Zero);

    /// <summary>A time as ConnectWise is sent it: UTC to the second, with a Z.</summary>
    public static string Text(DateTimeOffset at) =>
        at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// <summary>The shared publish contract's request with its example <c>started_at</c> replaced by <see cref="StartedAt"/>.</summary>
    public static string WithStartedAt(string request)
    {
        var node = JsonNode.Parse(request)!;
        node["started_at"] = Text(StartedAt);
        return node.ToJsonString();
    }
}
