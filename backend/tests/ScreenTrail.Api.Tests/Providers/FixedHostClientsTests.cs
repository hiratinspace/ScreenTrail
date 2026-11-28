using System.Net;
using Microsoft.Extensions.DependencyInjection;
using ScreenTrail.Api.Auth;
using ScreenTrail.Api.Providers.Llm;

namespace ScreenTrail.Api.Tests.Providers;

/// <summary>
/// The clients that call one fixed host: Gemini, Claude and Google's signing keys (security review E-11,
/// with K5). Each carries a key or a customer's redacted session, so a redirect must come back to the
/// caller rather than be followed with the key header and the body to whatever host it names, no proxy
/// from the machine's environment may sit in between, no cookie is kept, and an answer is held in memory
/// only up to a limit. Nothing here replaces a handler: what is under test is what Program.cs builds.
/// </summary>
public sealed class FixedHostClientsTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    /// <summary>The most of one answer these clients hold in memory: a draft or a key set is far smaller.</summary>
    private const long Cap = 8 * 1024 * 1024;

    public static TheoryData<string> Names() =>
    [
        nameof(GeminiProvider),
        nameof(ClaudeProvider),
        GoogleSigningKeys.HttpClientName,
    ];

    [Theory]
    [MemberData(nameof(Names))]
    public void EachFollowsNoRedirectAndUsesNoProxyOrCookies(string name)
    {
        var factory = api.Services.GetRequiredService<IHttpMessageHandlerFactory>();

        var primary = Primary(factory.CreateHandler(name));

        var sockets = Assert.IsType<SocketsHttpHandler>(primary);
        Assert.False(sockets.AllowAutoRedirect, $"{name} follows redirects");
        Assert.False(sockets.UseProxy, $"{name} uses a proxy from the environment");
        Assert.False(sockets.UseCookies, $"{name} keeps cookies");
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void EachHoldsAtMostEightMegabytesOfAnAnswer(string name)
    {
        using var client = api.Services.GetRequiredService<IHttpClientFactory>().CreateClient(name);

        Assert.InRange(client.MaxResponseContentBufferSize, 1, Cap);
    }

    [Fact]
    public async Task ARedirectComesBackToTheCallerAndTheOtherHostIsNeverCalled()
    {
        // The second host answers too, so a client that follows the redirect ends with its 200 rather
        // than an error: the failure, before the fix, is that answer.
        using var elsewhere = new LoopbackListener("HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        using var provider = new LoopbackListener($"HTTP/1.1 307 Temporary Redirect\r\nLocation: http://127.0.0.1:{elsewhere.Port}/landed\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        using var client = api.Services.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(GeminiProvider));
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"http://127.0.0.1:{provider.Port}/v1beta/models/m:generateContent"))
        {
            Content = new StringContent("{}"),
        };
        request.Headers.Add("x-goog-api-key", "made-up-for-this-test");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.TemporaryRedirect, response.StatusCode);
        Assert.Equal(0, await elsewhere.ConnectionsBeforeNowAsync(TestContext.Current.CancellationToken));
    }

    private static HttpMessageHandler Primary(HttpMessageHandler handler)
    {
        var at = handler;
        while (at is DelegatingHandler delegating && delegating.InnerHandler is { } inner)
        {
            at = inner;
        }

        return at;
    }
}
