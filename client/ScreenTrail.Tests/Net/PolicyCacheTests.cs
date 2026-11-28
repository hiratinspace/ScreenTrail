using ScreenTrail.Core.Net;

namespace ScreenTrail.Tests.Net;

/// <summary>
/// The policy file kept between runs is read as strictly as the backend's answer (security review F-06): a
/// member missing or of the wrong type, or a blank version, is no policy at all rather than a field read
/// as false or zero. What this build and the one before the update fields wrote still loads.
/// </summary>
public sealed class PolicyCacheTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("st-policy-cache-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string File(string json)
    {
        var path = Path.Combine(_folder, "policy.json");
        System.IO.File.WriteAllText(path, json);
        return path;
    }

    [Theory]
    [InlineData("""{ "version": "p20260925140200000", "retentionDays": 3, "localOnly": true, "captureAllWindows": false }""")]
    [InlineData("""{ "version": "p20260925140200000", "retentionDays": "3", "localOnly": true, "localOnlyLocked": true, "captureAllWindows": false }""")]
    [InlineData("""{ "version": " ", "retentionDays": 3, "localOnly": true, "localOnlyLocked": true, "captureAllWindows": false }""")]
    public void ACachedPolicyTheBackendsAnswerWouldNotPassLoadsAsNull(string json) =>
        Assert.Null(new FilePolicyCache(File(json)).Load());

    [Fact]
    public void TheCacheThisBuildWritesLoadsBack()
    {
        var path = Path.Combine(_folder, "policy.json");
        var policy = new TenantPolicy("p20260925140200000", 3, true, true, false, "pilot", true, 6, true);
        new FilePolicyCache(path).Save(policy);

        Assert.Equal(policy, new FilePolicyCache(path).Load());
    }

    [Fact]
    public void ACacheFromBeforeTheUpdateFieldsStillLoadsWithTheirDefaults()
    {
        var loaded = new FilePolicyCache(File("""
            {
              "version": "p20260925140200000",
              "retentionDays": 3,
              "localOnly": true,
              "localOnlyLocked": true,
              "captureAllWindows": false
            }
            """)).Load();

        Assert.Equal(new TenantPolicy("p20260925140200000", 3, true, true, false), loaded);
    }
}
