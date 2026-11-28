using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ScreenTrail.Core.Update;

namespace ScreenTrail.Tests.Update;

/// <summary>
/// "The same publisher" by identity, not by leaf certificate (ADR-0007's seam for ST-113). Artifact
/// Signing rotates its leaf every few days, so these build certificates at run time that share or do not
/// share a subject and an identity-validation EKU, with different keys and so different thumbprints.
/// That the chain is trusted is Windows' half, held by the Windows suite.
/// </summary>
public sealed class PublisherIdentityTests
{
    private const string Ours = "CN=ScreenTrail Test Publisher, O=ScreenTrail Test Publisher, L=Bloomington, S=Illinois, C=US";
    private const string Validation = PublisherIdentity.ArtifactSigningOidPrefix + "1.12345.1";

    [Fact]
    public void ANewLeafForTheSamePublisherIsTheSamePublisher()
    {
        using var today = Certificate(Ours, Validation);
        using var nextWeek = Certificate(Ours, Validation);
        var running = PublisherIdentity.From(today, timestamped: true);
        var package = PublisherIdentity.From(nextWeek, timestamped: true);

        Assert.NotEqual(running.Thumbprint, package.Thumbprint);
        Assert.Null(PublisherIdentity.Mismatch(running, package));
    }

    [Fact]
    public void AnotherSubjectIsAnotherPublisher()
    {
        using var ours = Certificate(Ours, Validation);
        using var theirs = Certificate("CN=Somebody Else, O=Somebody Else, C=US", Validation);

        Assert.NotNull(PublisherIdentity.Mismatch(PublisherIdentity.From(ours, true), PublisherIdentity.From(theirs, true)));
    }

    [Fact]
    public void TheSameSubjectUnderAnotherIdentityValidationIsAnotherPublisher()
    {
        // Artifact Signing writes the identity validation into every certificate it issues. A subject
        // string can be matched by another validated organisation of the same name; the validation's OID
        // cannot.
        using var ours = Certificate(Ours, Validation);
        using var lookalike = Certificate(Ours, PublisherIdentity.ArtifactSigningOidPrefix + "1.99999.1");
        using var none = Certificate(Ours);

        Assert.NotNull(PublisherIdentity.Mismatch(PublisherIdentity.From(ours, true), PublisherIdentity.From(lookalike, true)));
        Assert.NotNull(PublisherIdentity.Mismatch(PublisherIdentity.From(ours, true), PublisherIdentity.From(none, true)));
    }

    [Fact]
    public void WithoutAValidationOidTheSubjectAloneIsNotEnough()
    {
        // A certificate from the Windows store (a hardware token) carries no Artifact Signing OID, and its
        // subject is text any trusted authority may write. Another key under the same subject is someone
        // else, whatever the subject says.
        using var first = Certificate(Ours);
        using var lookalike = Certificate(Ours);

        Assert.NotNull(PublisherIdentity.Mismatch(PublisherIdentity.From(first, true), PublisherIdentity.From(lookalike, true)));
    }

    [Fact]
    public void WithoutAValidationOidTheSameKeyUnderTheSameSubjectIsTheSamePublisher()
    {
        // A renewal that keeps the key (a new certificate for the same hardware token key) still updates.
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var first = Certificate(Ours, key);
        using var renewed = Certificate(Ours, key);
        using var renamed = Certificate("CN=Somebody Else, O=Somebody Else, C=US", key);
        var running = PublisherIdentity.From(first, true);

        Assert.NotEqual(first.Thumbprint, renewed.Thumbprint);
        Assert.Null(PublisherIdentity.Mismatch(running, PublisherIdentity.From(renewed, true)));
        Assert.NotNull(PublisherIdentity.Mismatch(running, PublisherIdentity.From(renamed, true)));
    }

    [Fact]
    public void APackageSignedWithoutATimestampIsRefused()
    {
        // A short-lived certificate without a timestamp stops verifying within days; a package signed
        // that way was not signed by the release pipeline (sign.ps1 refuses to make one).
        using var today = Certificate(Ours, Validation);
        using var package = Certificate(Ours, Validation);

        var reason = PublisherIdentity.Mismatch(PublisherIdentity.From(today, true), PublisherIdentity.From(package, timestamped: false));

        Assert.NotNull(reason);
        Assert.Contains("timestamp", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ADevelopmentBuildOrAnUnacceptedPackageHasNoPublisherToCompare()
    {
        using var ours = Certificate(Ours, Validation);
        var identity = PublisherIdentity.From(ours, true);

        Assert.Contains("development build", PublisherIdentity.Mismatch(null, identity), StringComparison.Ordinal);
        Assert.NotNull(PublisherIdentity.Mismatch(identity, null));
    }

    [Fact]
    public void OnlyTheIdentityValidationUsagesAreRead()
    {
        using var certificate = Certificate(Ours, Validation, "1.3.6.1.5.5.7.3.3");

        Assert.Equal([Validation], PublisherIdentity.From(certificate, true).ValidationOids);
    }

    [Fact]
    public void ThePipesPeerSignedWithANewLeafOfTheSamePublisherIsTheSamePublisher()
    {
        // B-11: a service from one build and a UI from the next carry different short-lived leaves of one
        // publisher. The pipe's "same publisher" is the identity the updater compares, not the leaf.
        using var service = Certificate(Ours, Validation);
        using var ui = Certificate(Ours, Validation);
        var running = PublisherIdentity.From(service, timestamped: true);
        var peer = PublisherIdentity.From(ui, timestamped: true);

        Assert.NotEqual(running.Thumbprint, peer.Thumbprint);
        Assert.True(PublisherIdentity.SamePublisher(running, peer));
    }

    [Fact]
    public void ThePipesPeerIsHeldToTheSameIdentityAsAnUpdate()
    {
        using var ours = Certificate(Ours, Validation);
        using var theirs = Certificate("CN=Somebody Else, O=Somebody Else, C=US", Validation);
        using var lookalike = Certificate(Ours, PublisherIdentity.ArtifactSigningOidPrefix + "1.99999.1");
        using var none = Certificate(Ours);
        using var otherKey = Certificate(Ours);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var first = Certificate(Ours, key);
        using var renewed = Certificate(Ours, key);
        var running = PublisherIdentity.From(ours, true);

        Assert.False(PublisherIdentity.SamePublisher(running, PublisherIdentity.From(theirs, true)));
        Assert.False(PublisherIdentity.SamePublisher(running, PublisherIdentity.From(lookalike, true)));
        Assert.False(PublisherIdentity.SamePublisher(running, PublisherIdentity.From(none, true)));
        Assert.False(PublisherIdentity.SamePublisher(PublisherIdentity.From(none, true), PublisherIdentity.From(otherKey, true)));
        Assert.True(PublisherIdentity.SamePublisher(PublisherIdentity.From(first, true), PublisherIdentity.From(renewed, false)));
    }

    [Theory]
    [InlineData("WindowsClientVerifier.cs")]
    [InlineData("WindowsServerVerifier.cs")]
    public void BothPipeVerifiersCompareThePublisherNotTheLeaf(string file)
    {
        // B-11: both ends of the pipe read who signed each executable as Windows walked the chain
        // (Authenticode.Publisher) and compare it as the updater does. The same-folder rule stays.
        var source = File.ReadAllText(Source("ScreenTrail.Platform", "Ipc", file));

        Assert.DoesNotContain("VerifiedSignerThumbprint", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Thumbprint", source, StringComparison.Ordinal);
        Assert.Contains("Authenticode.Publisher(", source, StringComparison.Ordinal);
        Assert.Contains("PublisherIdentity.SamePublisher(", source, StringComparison.Ordinal);
        Assert.Contains("PeerDirectory.Same(", source, StringComparison.Ordinal);
        Assert.Contains("own_signature_unverifiable", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSigningStepChecksTheTwoExecutablesShareAPublisherNotALeaf()
    {
        // B-11: sign.ps1 held the two executables to one leaf certificate. It now holds them to the
        // identity the peer checks compare.
        var sign = File.ReadAllText(Source("..", "installer", "sign.ps1"));

        Assert.DoesNotContain("SignerCertificate.Thumbprint } | Sort-Object -Unique", sign, StringComparison.Ordinal);
        Assert.Contains("if (-not (Test-SamePublisher $signatures[$exes[0]].SignerCertificate $signatures[$exes[1]].SignerCertificate)) {", sign, StringComparison.Ordinal);
    }

    private static string Source(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.GetFullPath(Path.Combine([directory.FullName, "client", .. parts]));
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"{string.Join('/', parts)} was not found above {AppContext.BaseDirectory}; these tests run from a checkout.");
    }

    private static X509Certificate2 Certificate(string subject, params string[] usages)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return Certificate(subject, key, usages);
    }

    private static X509Certificate2 Certificate(string subject, ECDsa key, params string[] usages)
    {
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256);
        if (usages.Length > 0)
        {
            var oids = new OidCollection();
            foreach (var usage in usages)
            {
                _ = oids.Add(new Oid(usage));
            }

            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(oids, critical: false));
        }

        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(3));
    }
}
