using System.Net;
using ScreenTrail.Core.Net;

namespace ScreenTrail.Tests.Net;

/// <summary>
/// I-01: the service's backend client reads no answer whole past <see cref="BackendHttp.LongestAnswer"/>,
/// 8 MiB. Every caller reads its answer whole, and HttpClient's own limit is 2 GB. Local code only: the
/// handler stands in for a host answering as the backend, and no socket is opened.
/// </summary>
public sealed class BackendHttpTests
{
    private const long Sent = 64L * 1024 * 1024;

    [Fact]
    public void TheBackendClientHoldsNoAnswerLargerThanEightMiB()
    {
        using var http = BackendHttp.Create(new Answering(() => new EndlessSpaces(0)), new Uri("https://backend.invalid/"));

        Assert.InRange(http.MaxResponseContentBufferSize, 1, 8L * 1024 * 1024);
        Assert.Equal(new Uri("https://backend.invalid/"), http.BaseAddress);
    }

    [Fact(Timeout = 60_000)]
    public async Task ThePolicyFetchStopsReadingAnAnswerFarLargerThanAnyPolicy()
    {
        var body = new EndlessSpaces(Sent);
        using var http = BackendHttp.Create(new Answering(() => body), new Uri("https://backend.invalid/"));
        var sync = new PolicySync(http, () => "token", new NoCache());

        var fetched = await sync.FetchAsync(TestContext.Current.CancellationToken);

        Assert.False(fetched);
        Assert.True(body.Given <= BackendHttp.LongestAnswer + (1024 * 1024), $"The fetch read {body.Given} bytes of a {Sent}-byte answer.");
    }

    [Fact(Timeout = 60_000)]
    public async Task EveryRequestStillGoesThroughTheHandlerThatDecidesIt()
    {
        // The egress guard is the handler: the cap must not put anything between it and a request.
        var ct = TestContext.Current.CancellationToken;
        var handler = new Answering(() => new EndlessSpaces(2));
        using var http = BackendHttp.Create(handler, new Uri("https://backend.invalid/"));

        _ = await http.GetStringAsync(new Uri("v1/a", UriKind.Relative), ct);
        _ = await http.GetStringAsync(new Uri("v1/b", UriKind.Relative), ct);

        Assert.Equal(["/v1/a", "/v1/b"], handler.Paths);
    }

    [Fact]
    public void TheServiceBuildsItsBackendClientThroughTheFactory()
    {
        var host = File.ReadAllText(Path.Combine(Repository(), "client", "ScreenTrail.Service", "Host", "CaptureHost.cs"));

        Assert.Contains("BackendHttp.Create(egress, backend)", host, StringComparison.Ordinal);
        Assert.DoesNotContain("new HttpClient(egress) { BaseAddress", host, StringComparison.Ordinal);
    }

    private static string Repository()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "client", "ScreenTrail.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Not inside the repository.");
    }

    private sealed class NoCache : IPolicyCache
    {
        public TenantPolicy? Load() => null;

        public void Save(TenantPolicy policy)
        {
        }
    }

    private sealed class Answering(Func<Stream> body) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (Paths)
            {
                Paths.Add(request.RequestUri!.AbsolutePath);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body()) });
        }
    }

    /// <summary>Whitespace, which is valid before a JSON value, so a parser would keep reading too.</summary>
    private sealed class EndlessSpaces(long length) : Stream
    {
        public long Given { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => Given; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = (int)Math.Min(count, length - Given);
            Array.Fill(buffer, (byte)' ', offset, n);
            Given += n;
            return n;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
