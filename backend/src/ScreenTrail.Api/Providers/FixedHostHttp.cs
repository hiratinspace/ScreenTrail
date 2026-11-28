namespace ScreenTrail.Api.Providers;

/// <summary>
/// The handler and the answer limit of the clients that call one fixed host: Gemini, Claude and Google's
/// signing keys (security review E-11, with K5). Program.cs applies both to each of them. The PSA and
/// documentation clients have their own, stricter handler (<see cref="PublicNetwork.Handler"/>).
///
/// No redirects, because a redirect would carry the provider key header and a customer's redacted
/// session to whatever host it names (.NET drops only <c>Authorization</c>); no proxy, because one taken
/// from the machine's environment would see both; no cookies, because nothing these calls do needs one.
/// </summary>
public static class FixedHostHttp
{
    /// <summary>The most of one answer held in memory. A draft or a key set is a few kilobytes.</summary>
    public const long MaxResponseBytes = 8 * 1024 * 1024;

    public static SocketsHttpHandler Handler() => new()
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        UseCookies = false,
    };

    /// <summary>The answer limit, beside whatever base address and deadline the client already has.</summary>
    public static void Limit(HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        client.MaxResponseContentBufferSize = MaxResponseBytes;
    }
}
