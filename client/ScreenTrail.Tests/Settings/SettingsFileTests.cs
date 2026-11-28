using ScreenTrail.Core.Net;
using ScreenTrail.Core.Settings;
using ScreenTrail.Shared.Settings;

namespace ScreenTrail.Tests.Settings;

/// <summary>
/// The settings and capture settings files are read back at every start. The pipe checks a document before
/// it saves one; a file that was changed or damaged on disk has to meet the same rules when it is read, and
/// one that does not, or that cannot be read at all, is taken at its strictest rather than as the defaults
/// (DEC-06). Started from the security review's own tests of the load path.
/// </summary>
public sealed class SettingsFileTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("st-settings-file-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void ASettingsFileThatBreaksThePipesRulesLoadsAsTheStrictDocument()
    {
        var path = Path.Combine(_folder, "settings.json");
        File.WriteAllText(path, """
            { "local_only": false, "retention_days": 3650, "ssn": false, "cards": false, "api_keys": false, "passwords": false,
              "custom_patterns": ["("], "excluded_processes": ["C:\\Tools\\keepass.exe"] }
            """);

        var loaded = new ClientSettingsStore(path).Load();

        Assert.Empty(loaded.Problems());
        Assert.True(loaded.LocalOnly);
        Assert.True(loaded.Ssn && loaded.Cards && loaded.ApiKeys && loaded.Passwords && loaded.Emails);
        Assert.False(loaded.Telemetry);
    }

    [Fact]
    public void RetentionReadFromTheFileStaysInsideTheDocumentedOneToThirtyDays()
    {
        var path = Path.Combine(_folder, "settings.json");
        File.WriteAllText(path, """{ "retention_days": 3650 }""");

        var applied = PolicyApplication.Resolve(TenantPolicy.Default, new ClientSettingsStore(path).Load());

        Assert.InRange(applied.Retention.TotalDays, ClientSettings.MinRetentionDays, ClientSettings.MaxRetentionDays);
    }

    [Fact]
    public void ASettingsFileThatCannotBeParsedDoesNotLiftTheTechniciansOwnProtections()
    {
        var path = Path.Combine(_folder, "settings.json");
        var store = new ClientSettingsStore(path);
        Assert.True(store.Save(new ClientSettings { LocalOnly = true, Emails = true, CustomPatterns = ["ACME-[0-9]{6}"], ExcludedProcesses = ["acmevault"] }));

        // One byte of damage (a torn sync, a disk fault, a same-user process).
        File.WriteAllText(path, "{" + File.ReadAllText(path));

        var loaded = store.Load();

        Assert.True(
            loaded.LocalOnly && loaded.Emails && loaded.CustomPatterns.Count == 1 && loaded.ExcludedProcesses.Count == 1,
            $"After the file was damaged the service starts with local_only={loaded.LocalOnly}, emails={loaded.Emails}, {loaded.CustomPatterns.Count} custom pattern(s), {loaded.ExcludedProcesses.Count} excluded process(es).");
        Assert.True(loaded.Ssn && loaded.Cards && loaded.ApiKeys && loaded.Passwords);
    }

    [Fact]
    public void ASettingsFileThatCannotBeParsedAndHasNoEarlierCopyIsStillTheStrictDocument()
    {
        var path = Path.Combine(_folder, "settings.json");
        File.WriteAllText(path, "{ not json");

        var loaded = new ClientSettingsStore(path).Load();

        Assert.True(loaded.LocalOnly);
        Assert.True(loaded.Ssn && loaded.Cards && loaded.ApiKeys && loaded.Passwords && loaded.Emails);
        Assert.Empty(loaded.Problems());
    }

    [Fact]
    public void ASettingsFileTheServiceWroteLoadsAsItWasWritten()
    {
        // What a save over the pipe wrote, by this build or the one before it, is read back unchanged.
        var path = Path.Combine(_folder, "settings.json");
        var saved = new ClientSettings { LocalOnly = false, RetentionDays = 12, Passwords = false, Telemetry = true, CustomPatterns = ["ACME-[0-9]{6}"], ExcludedProcesses = ["acmevault"] };
        File.WriteAllText(path, """
            {
              "local_only": false,
              "telemetry": true,
              "retention_days": 12,
              "ssn": true,
              "cards": true,
              "api_keys": true,
              "passwords": false,
              "emails": false,
              "custom_patterns": [ "ACME-[0-9]{6}" ],
              "excluded_processes": [ "acmevault" ]
            }
            """);

        var loaded = new ClientSettingsStore(path).Load();

        Assert.Equal(saved.LocalOnly, loaded.LocalOnly);
        Assert.Equal(saved.RetentionDays, loaded.RetentionDays);
        Assert.Equal(saved.Passwords, loaded.Passwords);
        Assert.Equal(saved.Telemetry, loaded.Telemetry);
        Assert.Equal(saved.CustomPatterns, loaded.CustomPatterns);
        Assert.Equal(saved.ExcludedProcesses, loaded.ExcludedProcesses);
    }

    [Fact]
    public void NoSettingsFileIsTheDefaultsAsBefore()
    {
        var loaded = new ClientSettingsStore(Path.Combine(_folder, "settings.json")).Load();

        Assert.Equal(new ClientSettings().LocalOnly, loaded.LocalOnly);
        Assert.Equal(new ClientSettings().Emails, loaded.Emails);
        Assert.Equal(new ClientSettings().RetentionDays, loaded.RetentionDays);
    }

    [Fact]
    public void ACaptureFileThatBreaksThePipesRulesLoadsAsTheStrictDocument()
    {
        var path = Path.Combine(_folder, "capture.json");
        File.WriteAllText(path, """{ "capture_all_windows": true, "grace_seconds": 2000000000, "sensitivity": "extreme", "speech_model": "../../evil" }""");

        var loaded = new CaptureSettingsStore(path).Load();

        Assert.Empty(loaded.Problems());
        Assert.False(loaded.CaptureAllWindows);
    }

    [Fact]
    public void ACaptureFileThatCannotBeParsedDoesNotWidenWhatIsCaptured()
    {
        var path = Path.Combine(_folder, "capture.json");
        var store = new CaptureSettingsStore(path);
        Assert.True(store.Save(new CaptureSettings { DisabledTools = ["anydesk"], GraceSeconds = 60 }));
        File.WriteAllText(path, "{" + File.ReadAllText(path));

        var loaded = store.Load();

        Assert.False(loaded.CaptureAllWindows);
        Assert.Equal(["anydesk"], loaded.DisabledTools);
        Assert.Empty(loaded.Problems());
    }
}
