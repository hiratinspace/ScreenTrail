namespace ScreenTrail.Api.Endpoints;

/// <summary>How much of a request body the authenticated groups read when a route does not say (D-01).</summary>
public static class BodyLimits
{
    /// <summary>
    /// The default for every route under <c>/v1</c> and the owner's <c>/v1/admin</c>: a metric, an invite,
    /// a policy or a mapping is a few fields and fits in far less. A route that reads more says so itself.
    /// </summary>
    public const long Default = 16 * 1024;
}
