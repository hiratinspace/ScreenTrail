using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;

namespace ScreenTrail.Api.Tests.Vault;

/// <summary>
/// The vault's keys are checked when the host starts (security review E-08): a key that is set must be 32
/// bytes of base64, and the previous key must not be the current one. A host that started with either
/// answered every publish, ticket search and Test connection with a server error instead.
/// </summary>
public sealed class VaultStartupTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    public static TheoryData<string> Malformed() =>
    [
        "not base64 at all",
        Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)),
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)),
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
    ];

    [Theory]
    [MemberData(nameof(Malformed))]
    public void AMasterKeyThatIsNotThirtyTwoBytesOfBase64StopsTheHost(string key)
    {
        using var host = api.WithWebHostBuilder(builder => builder.UseSetting("Vault:MasterKey", key));

        var thrown = Record.Exception(() => host.Server);

        Assert.NotNull(thrown);
        Assert.Contains("Vault:MasterKey", Flatten(thrown), StringComparison.Ordinal);
        Assert.DoesNotContain(key, Flatten(thrown), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Malformed))]
    public void APreviousKeyThatIsNotThirtyTwoBytesOfBase64StopsTheHost(string key)
    {
        using var host = api.WithWebHostBuilder(builder => builder.UseSetting("Vault:PreviousMasterKey", key));

        var thrown = Record.Exception(() => host.Server);

        Assert.NotNull(thrown);
        Assert.Contains("Vault:PreviousMasterKey", Flatten(thrown), StringComparison.Ordinal);
        Assert.DoesNotContain(key, Flatten(thrown), StringComparison.Ordinal);
    }

    [Fact]
    public void ThePreviousKeySetToTheCurrentKeyStopsTheHost()
    {
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        using var host = api.WithWebHostBuilder(builder =>
        {
            _ = builder.UseSetting("Vault:MasterKey", key);
            _ = builder.UseSetting("Vault:PreviousMasterKey", key);
        });

        var thrown = Record.Exception(() => host.Server);

        Assert.NotNull(thrown);
        Assert.Contains("Vault:PreviousMasterKey", Flatten(thrown), StringComparison.Ordinal);
        Assert.DoesNotContain(key, Flatten(thrown), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TwoDifferentKeysAndNoKeyAtAllBothStillServe()
    {
        // The controls: a rotation's two keys start, and so does a host with no vault key, which says
        // the vault is not configured when asked to store (ST-009).
        using var rotating = api.WithWebHostBuilder(builder =>
        {
            _ = builder.UseSetting("Vault:MasterKey", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
            _ = builder.UseSetting("Vault:PreviousMasterKey", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        });
        using var none = api.WithWebHostBuilder(builder => builder.UseSetting("Vault:MasterKey", string.Empty));

        foreach (var host in new[] { rotating, none })
        {
            using var client = host.CreateClient();
            using var response = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    private static string Flatten(Exception? thrown)
    {
        var messages = new List<string>();
        for (var at = thrown; at is not null; at = at.InnerException)
        {
            messages.Add(at.Message);
        }

        return string.Join(" | ", messages);
    }
}
