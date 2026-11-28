namespace ScreenTrail.Core.Net;

/// <summary>
/// The one client the service talks to its backend with: policy, the device's sign-in, drafts, PSA relays
/// and metrics (security review I-01).
///
/// Every one of those reads its answer whole before it looks at it, and HttpClient's own limit on that is
/// 2 GB, so a host the device trusts could have the capture service read until it ran out of memory in the
/// middle of a session. The largest honest answer is a PSA relay, which the backend caps at 4 MiB; the
/// client takes at most <see cref="LongestAnswer"/>, and an answer past it is the
/// <see cref="HttpRequestException"/> every caller already reads as "no answer".
///
/// Only the size is set here. The handler is the egress guard, which decides each request and each
/// redirect hop as it is made; nothing about that is decided once, here.
/// </summary>
public static class BackendHttp
{
    /// <summary>The most of one answer read into memory: twice the backend's own cap on a PSA relay.</summary>
    public const long LongestAnswer = 8L * 1024 * 1024;

    /// <param name="handler">The egress guard, or what stands in for it in a test.</param>
    /// <param name="baseAddress">The backend's address; null when none is configured.</param>
    public static HttpClient Create(HttpMessageHandler handler, Uri? baseAddress)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return new HttpClient(handler) { BaseAddress = baseAddress, MaxResponseContentBufferSize = LongestAnswer };
    }
}
